using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Outspoken.Core.Overlay;

namespace Outspoken.App.Overlay;

/// <summary>
/// Renders the overlay pill in each state to PNG assets for the README / PR — the same
/// <see cref="PillVisual"/> the live overlay uses, so the images are the real thing, not a
/// mockup. Run via `Outspoken.App.exe showcase &lt;outputDir&gt;`.
/// </summary>
public static class ShowcaseRenderer
{
    private const double Scale = 2.0; // 2x for crisp retina-quality assets

    private sealed record Shot(string Name, string Caption, Action<PillVisual> Configure, double Width = OverlayTheme.PillWidth);

    public static void RenderAll(string outputDir)
    {
        Directory.CreateDirectory(outputDir);

        var shots = new Shot[]
        {
            new("listening", "Listening", p => p.RenderStaticFrame(PillMode.Listening, 0.7f)),
            new("processing", "Processing", p => p.RenderStaticFrame(PillMode.Processing, 0.4f)),
            new("cleaned", "Cleaned", p => p.RenderStaticFrame(PillMode.Done, 0.1f)),
            new("raw", "Raw fallback", p => { p.RenderStaticFrame(PillMode.Done, 0.1f); p.ShowTag("raw"); }),
            new("clipboard", "Clipboard fallback", p => p.ShowCenteredMessage("Copied — press Ctrl+V", OverlayTheme.AmberDeep), OverlayTheme.PillWidth + 40),
            new("error", "No speech", p => p.ShowCenteredMessage("no speech heard", OverlayTheme.ErrorRed)),
        };

        // Individual transparent-background PNGs (shadow preserved).
        foreach (var shot in shots)
            SavePng(BuildPill(shot), Path.Combine(outputDir, $"pill-{shot.Name}.png"));

        // Hero "states board" on a warm-charcoal presentation backdrop.
        SavePng(BuildBoard(shots), Path.Combine(outputDir, "overlay-states.png"));

        // Brand lockup (mark + wordmark) on the same warm-charcoal backdrop.
        SavePng(BuildBrandLockup(), Path.Combine(outputDir, "logo.png"));

        Console.WriteLine($"Wrote {shots.Length + 2} images to {outputDir}");
    }

    /// <summary>The brand lockup: the "Mic &amp; Halo" squircle mark beside the wordmark, on the
    /// warm-charcoal presentation backdrop — the real <see cref="BrandMark"/>, not a mockup.</summary>
    private static FrameworkElement BuildBrandLockup()
    {
        var mark = new Viewbox
        {
            Width = 104,
            Height = 104,
            Child = BrandMark.CreateVisual(withGround: true),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24, 0, 0, 0) };
        words.Children.Add(new TextBlock
        {
            Text = "Outspoken",
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 46,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xF1, 0xE9, 0xDC)),
        });
        words.Children.Add(new TextBlock
        {
            Text = "Dictation without limits",
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 17,
            Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xA4, 0x91)),
            Margin = new Thickness(2, 4, 0, 0),
        });

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(mark);
        row.Children.Add(words);

        var board = new Border
        {
            Padding = new Thickness(56, 44, 72, 44),
            Background = new LinearGradientBrush(
                Color.FromRgb(0x24, 0x22, 0x1F), Color.FromRgb(0x1A, 0x18, 0x16), 90),
            Child = row,
        };
        Layout(board);
        return board;
    }

    private static FrameworkElement BuildPill(Shot shot)
    {
        var pill = new PillVisual { Width = shot.Width };
        var host = new Border { Padding = new Thickness(22), Background = Brushes.Transparent, Child = pill };
        Layout(host);
        shot.Configure(pill);   // configure after layout so the waveform has a real width
        Layout(host);
        return host;
    }

    private static FrameworkElement BuildBoard(IReadOnlyList<Shot> shots)
    {
        var rows = new StackPanel { Orientation = Orientation.Vertical };
        foreach (var shot in shots)
        {
            var pill = new PillVisual { Width = shot.Width };
            var label = new TextBlock
            {
                Text = shot.Caption.ToUpperInvariant(),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xC2, 0xB6)),
                Width = 150,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 14) };
            row.Children.Add(label);
            var pillHost = new Border { Width = OverlayTheme.PillWidth + 60, Child = pill, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(pillHost);
            rows.Children.Add(row);

            Layout(rows);
            shot.Configure(pill);
        }

        var board = new Border
        {
            Padding = new Thickness(40, 30, 60, 30),
            Background = new LinearGradientBrush(
                Color.FromRgb(0x24, 0x22, 0x1F), Color.FromRgb(0x1A, 0x18, 0x16), 90),
            Child = rows,
        };
        Layout(board);
        foreach (var shot in shots.Zip(rows.Children.OfType<StackPanel>()))
        {
            var pill = shot.Second.Children.OfType<Border>().First().Child as PillVisual;
            shot.First.Configure(pill!); // re-apply after final layout so waveforms use the arranged width
        }
        Layout(board);
        return board;
    }

    private static void Layout(FrameworkElement el)
    {
        el.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        el.Arrange(new Rect(el.DesiredSize));
        el.UpdateLayout();
    }

    private static void SavePng(FrameworkElement el, string path)
    {
        var w = (int)Math.Ceiling(el.ActualWidth * Scale);
        var h = (int)Math.Ceiling(el.ActualHeight * Scale);
        var rtb = new RenderTargetBitmap(w, h, 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
        rtb.Render(el);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }
}
