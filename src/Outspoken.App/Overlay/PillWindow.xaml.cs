using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Outspoken.Core.Overlay;

namespace Outspoken.App.Overlay;

/// <summary>
/// The overlay pill window (Design Direction) — a layered, transparent, always-on-top,
/// click-through window that never steals focus. Owns the window/interop concerns and a
/// 60fps render loop; the pixels come from the hosted <see cref="PillVisual"/>.
///
/// Public methods must be called on the UI thread (the app marshals orchestrator events
/// via the Dispatcher first).
/// </summary>
public partial class PillWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20;      // click-through
    private const int WS_EX_NOACTIVATE = 0x08000000; // never take focus
    private const int WS_EX_TOOLWINDOW = 0x80;       // no alt-tab entry

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private PillMode _mode = PillMode.Hidden;
    private Func<float>? _levelProvider;
    private TimeSpan _modeEnteredAt;
    private bool _rendering;

    public PillWindow()
    {
        InitializeComponent();
        Width = OverlayTheme.PillWidth + OverlayTheme.WidthGrowthMax + 32; // room for growth + shadow
        Height = OverlayTheme.PillHeight + 32;
        Opacity = 0;
        SourceInitialized += (_, _) => ApplyClickThroughStyles();
    }

    public void ShowListening(Func<float> levelProvider)
    {
        _levelProvider = levelProvider;
        Visual.ResetChrome();
        SetMode(PillMode.Listening);
        PositionBottomCenter();
        FadeTo(1.0, 120);
        EnsureRendering();
    }

    public void ShowProcessing() => SetMode(PillMode.Processing);

    public void ShowDone(bool rawMode)
    {
        SetMode(PillMode.Done);
        Visual.SetStrandColor(OverlayTheme.AmberDeep);
        if (rawMode)
            Visual.ShowTag("raw");
    }

    public void ShowClipboard() => ShowMessage("Copied — press Ctrl+V", OverlayTheme.AmberDeep, 2500);

    public void ShowError(string message) => ShowMessage(message, OverlayTheme.ErrorRed, 2200);

    private void ShowMessage(string text, string colorHex, int holdMs)
    {
        SetMode(PillMode.Message);
        PositionBottomCenter();
        Visual.ShowCenteredMessage(text, colorHex);
        FadeTo(1.0, 120);
        EnsureRendering();

        _ = Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(holdMs);
            if (_mode == PillMode.Message)
                FadeOutAndHide();
        });
    }

    private void SetMode(PillMode mode)
    {
        _mode = mode;
        _modeEnteredAt = _clock.Elapsed;
    }

    private void EnsureRendering()
    {
        if (_rendering)
            return;
        _rendering = true;
        CompositionTarget.Rendering += OnRendering;
        Show();
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var t = _clock.Elapsed.TotalSeconds;
        switch (_mode)
        {
            case PillMode.Listening:
                Visual.PushLevel(_levelProvider?.Invoke() ?? 0f);
                Visual.RenderWaveform(t, 1.0);
                GrowWidthWithVoice();
                break;
            case PillMode.Processing:
                Visual.Settle();
                Visual.RenderWaveform(t, 0.35);
                break;
            case PillMode.Done:
                Visual.Settle();
                Visual.RenderWaveform(t, 0.0);
                if (_clock.Elapsed - _modeEnteredAt > TimeSpan.FromMilliseconds(220))
                    FadeOutAndHide();
                break;
        }
    }

    private void GrowWidthWithVoice()
    {
        var target = OverlayTheme.PillWidth + OverlayTheme.WidthGrowthMax * Visual.Level;
        Visual.PillWidth += (target - Visual.PillWidth) * 0.15;
    }

    private void PositionBottomCenter()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2.0;
        Top = area.Bottom - Height - OverlayTheme.BottomMargin + 16; // +16 offsets the shadow margin
    }

    private void FadeTo(double opacity, int ms) =>
        BeginAnimation(OpacityProperty, new DoubleAnimation(opacity, TimeSpan.FromMilliseconds(ms)));

    private void FadeOutAndHide()
    {
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200));
        fade.Completed += (_, _) =>
        {
            if (_mode is PillMode.Done or PillMode.Message)
            {
                StopRendering();
                Hide();
                _mode = PillMode.Hidden;
                Visual.ResetChrome();
            }
        };
        BeginAnimation(OpacityProperty, fade);
    }

    private void StopRendering()
    {
        if (!_rendering)
            return;
        _rendering = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void ApplyClickThroughStyles()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(nint hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);
}
