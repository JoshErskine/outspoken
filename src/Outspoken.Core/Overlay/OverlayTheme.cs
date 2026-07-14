namespace Outspoken.Core.Overlay;

/// <summary>
/// Overlay dimensions + palette from the Design Direction. Sizes are deliberately
/// constants Josh can tune during dogfooding (sign-off: "starting point, not settled").
/// Colors are ARGB hex strings so the WPF layer parses them without depending on Core.
/// </summary>
public static class OverlayTheme
{
    // Pill geometry — tuned smaller/minimal per Josh's T9 review (2026-07-14).
    public const double PillWidth = 172;
    public const double PillHeight = 38;
    public const double PillCornerRadius = 19;      // full radius = height/2
    public const double BottomMargin = 24;          // ~24px above the taskbar
    public const double WidthGrowthMax = 26;        // extra width at sustained full voice

    // Palette (Design Direction §The language / §Where Outspoken becomes its own thing).
    public const string Cream = "#FAF8F3";          // pill surface
    public const string CreamEdge = "#F5F2EB";      // subtle gradient toward the rim
    public const string Ink = "#1F1F1D";            // near-black waveform strands (idle)
    public const string Amber = "#C2571B";          // accent — success pulse, raw tag
    public const string AmberDeep = "#A84A15";
    public const string ErrorRed = "#B23A3A";       // error jitter (muted, not alarming)

    // Waveform.
    public const int StrandCount = 5;
    public const int SamplesPerStrand = 56;
}
