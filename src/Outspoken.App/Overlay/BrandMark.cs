using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace Outspoken.App.Overlay;

/// <summary>
/// The Outspoken brand mark — "Mic &amp; Halo" (line-drawn studio mic over a solid amber halo).
/// Built as WPF vector geometry translated 1:1 from the design SVG (docs/logo-refs), so it's
/// crisp at any size and is the single source for the app icon, tray icon, and Settings logo.
/// Palette: amber #C2571B, ink #1A1815, cream #F1E9DC.
/// </summary>
public static class BrandMark
{
    private static SolidColorBrush Brush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));

    /// <summary>Builds the mark. With <paramref name="withGround"/>, includes the cream squircle
    /// tile (the app-icon variant); without, the halo + mic on transparent (for light surfaces).</summary>
    public static FrameworkElement CreateVisual(bool withGround)
    {
        var canvas = new Canvas { Width = 1024, Height = 1024 };

        if (withGround)
            canvas.Children.Add(new Rectangle
            {
                Width = 1024,
                Height = 1024,
                // Generous corner radius (~33%) so the rounding still reads at 16px tray size, where
                // the near-white cream tile would otherwise look square-cornered against white.
                RadiusX = 340,
                RadiusY = 340,
                Fill = Brush("#F1E9DC"),
            });

        // The mic is drawn in SVG-local coords, then scaled + centred. Enlarged vs the source SVG's
        // generous safe-area padding so the mic stays readable in the 16px tray (Josh's T-review).
        var group = new Canvas
        {
            RenderTransform = new TransformGroup
            {
                Children =
                {
                    new ScaleTransform(4.6, 4.6),
                    new TranslateTransform(12, 66),
                },
            },
        };

        // Amber halo behind the mic.
        group.Children.Add(new Path
        {
            Data = Geometry.Parse("M112 22 C138 22 156 40 156 62 C156 84 138 100 112 100 C86 100 68 84 68 62 C68 40 86 22 112 22 Z"),
            Fill = Brush("#C2571B"),
        });

        // Ink mic line-work.
        var ink = Brush("#1A1815");
        group.Children.Add(Stroke(new RectangleGeometry(new Rect(74, 38, 52, 94), 26, 26), ink)); // capsule
        group.Children.Add(Stroke(Geometry.Parse("M62 100 L62 112 C62 133 79 147 100 147 C121 147 138 133 138 112 L138 100"), ink)); // yoke
        group.Children.Add(Stroke(Geometry.Parse("M100 147 L100 170"), ink)); // stem
        group.Children.Add(Stroke(Geometry.Parse("M80 172 L120 172"), ink));  // base
        group.Children.Add(Stroke(Geometry.Parse("M90 68 h20 M90 80 h20 M90 92 h20"), ink)); // grille

        canvas.Children.Add(group);
        return new Viewbox { Child = canvas, Stretch = Stretch.Uniform };
    }

    private static Path Stroke(Geometry data, Brush ink) => new()
    {
        Data = data,
        Stroke = ink,
        StrokeThickness = 7,
        StrokeLineJoin = PenLineJoin.Round,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
    };

    /// <summary>Renders the mark to a PNG at the given pixel size.</summary>
    public static byte[] RenderPng(int size, bool withGround)
    {
        var visual = CreateVisual(withGround);
        visual.Measure(new Size(size, size));
        visual.Arrange(new Rect(0, 0, size, size));
        visual.UpdateLayout();

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }
}
