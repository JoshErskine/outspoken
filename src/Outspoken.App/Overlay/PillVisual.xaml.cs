using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Outspoken.Core.Overlay;

namespace Outspoken.App.Overlay;

public enum PillMode { Hidden, Listening, Processing, Done, Message }

/// <summary>
/// The pill's visual tree (cream capsule + thread-waveform + message), separated from the
/// window/interop concerns in <see cref="PillWindow"/>. Reused by the live overlay and by the
/// screenshot showcase, so both render pixel-identical chrome. Holds the <see cref="WaveformModel"/>
/// and the strand polylines; callers drive it frame by frame.
/// </summary>
public partial class PillVisual : UserControl
{
    private readonly WaveformModel _waveform = new(OverlayTheme.StrandCount, OverlayTheme.SamplesPerStrand);
    private readonly Polyline[] _strands;

    public PillVisual()
    {
        InitializeComponent();
        Pill.Height = OverlayTheme.PillHeight;
        Pill.Width = OverlayTheme.PillWidth;
        Pill.CornerRadius = new CornerRadius(OverlayTheme.PillCornerRadius);

        _strands = new Polyline[_waveform.StrandCount];
        for (var i = 0; i < _strands.Length; i++)
        {
            _strands[i] = new Polyline
            {
                Stroke = AmberBrush,
                StrokeThickness = 1.5,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            };
            WaveCanvas.Children.Add(_strands[i]);
        }
    }

    private static SolidColorBrush Brush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));

    private static readonly SolidColorBrush AmberBrush = Brush(OverlayTheme.Amber);

    public double Level => _waveform.Level;
    public double PillWidth { get => Pill.Width; set => Pill.Width = value; }

    public double PushLevel(float raw) => _waveform.PushLevel(raw);
    public void Settle() => _waveform.Settle();

    public void SetStrandColor(string hex)
    {
        var brush = Brush(hex);
        foreach (var s in _strands) s.Stroke = brush;
    }

    public void ShowStrands(bool visible)
    {
        var v = visible ? Visibility.Visible : Visibility.Collapsed;
        foreach (var s in _strands) s.Visibility = v;
    }

    /// <summary>Recomputes strand polylines for the current level at the given time/scale.</summary>
    public void RenderWaveform(double time, double amplitudeScale)
    {
        var width = Math.Max(0, WaveCanvas.ActualWidth);
        if (width <= 0)
            return;
        var strands = _waveform.ComputeStrands(width, OverlayTheme.PillHeight, time, amplitudeScale);
        for (var s = 0; s < _strands.Length; s++)
        {
            _strands[s].Visibility = Visibility.Visible;
            var pts = new PointCollection(strands[s].Length);
            foreach (var p in strands[s])
                pts.Add(new Point(p.X, p.Y));
            _strands[s].Points = pts;
        }
    }

    public void ShowCenteredMessage(string text, string colorHex)
    {
        ShowStrands(false);
        MessageText.Text = text;
        MessageText.FontSize = 13;
        MessageText.HorizontalAlignment = HorizontalAlignment.Center;
        MessageText.Margin = default;
        MessageText.Foreground = Brush(colorHex);
        MessageText.Visibility = Visibility.Visible;
    }

    public void ShowTag(string tag)
    {
        MessageText.Text = tag;
        MessageText.FontSize = 10;
        MessageText.HorizontalAlignment = HorizontalAlignment.Right;
        MessageText.Margin = new Thickness(0, 0, 14, 0);
        MessageText.Foreground = Brush(OverlayTheme.Amber);
        MessageText.Visibility = Visibility.Visible;
    }

    public void HideMessage() => MessageText.Visibility = Visibility.Collapsed;

    public void ResetChrome()
    {
        Pill.Width = OverlayTheme.PillWidth;
        HideMessage();
        ShowStrands(true);
        SetStrandColor(OverlayTheme.Amber);
    }

    /// <summary>
    /// Paints one representative static frame for a given state — used by the screenshot
    /// showcase so the README/PR images show the pill exactly as it renders live.
    /// </summary>
    public void RenderStaticFrame(PillMode mode, float level, double time = 0.28)
    {
        ResetChrome();
        for (var i = 0; i < 30; i++) _waveform.PushLevel(level); // settle to the target level
        Measure(new Size(OverlayTheme.PillWidth, OverlayTheme.PillHeight));
        Arrange(new Rect(0, 0, OverlayTheme.PillWidth, OverlayTheme.PillHeight));

        switch (mode)
        {
            case PillMode.Listening:
                SetStrandColor(OverlayTheme.Amber);
                RenderWaveform(time, 1.0);
                break;
            case PillMode.Processing:
                SetStrandColor(OverlayTheme.Amber);
                RenderWaveform(time, 0.35);
                break;
            case PillMode.Done:
                SetStrandColor(OverlayTheme.AmberDeep);
                RenderWaveform(time, 0.05);
                break;
        }
    }
}
