namespace Outspoken.Core.Overlay;

public readonly record struct WavePoint(double X, double Y);

/// <summary>
/// Pure geometry for the overlay's thread-waveform (Design Direction §Build notes):
/// 4–6 phase-offset sine strands whose amplitude tracks a smoothed mic level. No WPF
/// types, so the shape is unit-testable and the WPF control is a thin renderer.
///
/// The model is stateful only for amplitude smoothing (so the weave grows/settles
/// organically rather than snapping); geometry for a given (time, level) is otherwise
/// deterministic.
/// </summary>
public sealed class WaveformModel
{
    private readonly int _strandCount;
    private readonly int _samplesPerStrand;
    private readonly double _smoothing;
    private double _smoothedLevel;

    public WaveformModel(int strandCount = 5, int samplesPerStrand = 48, double smoothing = 0.25)
    {
        if (strandCount is < 1 or > 8)
            throw new ArgumentOutOfRangeException(nameof(strandCount), "Design Direction: 4–6 strands (cap 8).");
        _strandCount = strandCount;
        _samplesPerStrand = samplesPerStrand;
        _smoothing = Math.Clamp(smoothing, 0.01, 1.0);
    }

    public int StrandCount => _strandCount;

    /// <summary>Current smoothed level (0..1), exposed for the amber-tint mapping.</summary>
    public double Level => _smoothedLevel;

    /// <summary>
    /// Feed a raw mic RMS (0..1-ish); returns the smoothed value used for amplitude.
    /// Speech RMS is low (~0.1–0.2), so a sqrt gain maps normal speaking to a full weave
    /// while true silence stays near-flat (Josh's T9 review: "more emphasis when speaking").
    /// </summary>
    public double PushLevel(float rawLevel)
    {
        var clamped = Math.Clamp(rawLevel, 0f, 1f);
        var boosted = Math.Clamp(MathF.Sqrt(clamped) * 1.9f, 0f, 1f);
        _smoothedLevel += (boosted - _smoothedLevel) * _smoothing;
        return _smoothedLevel;
    }

    /// <summary>Collapse the weave toward flat (Processing settle / Done snap).</summary>
    public void Settle() => _smoothedLevel += (0 - _smoothedLevel) * _smoothing;

    /// <summary>
    /// Strand polylines across a pill of the given inner width/height, at animation time
    /// <paramref name="timeSeconds"/>. <paramref name="amplitudeScale"/> lets states damp the
    /// weave (1 = live listening, ~0.25 = processing shimmer, 0 = flat line).
    /// </summary>
    public IReadOnlyList<WavePoint[]> ComputeStrands(double width, double height, double timeSeconds, double amplitudeScale = 1.0)
    {
        var centerY = height / 2.0;
        // Idle threads are barely-moving (minimal at rest); speech opens them right up.
        var idleAmp = height * 0.045;
        var voiceAmp = height * 0.40 * _smoothedLevel;
        var amp = (idleAmp + voiceAmp) * Math.Clamp(amplitudeScale, 0.0, 1.0);

        var strands = new WavePoint[_strandCount][];
        for (var s = 0; s < _strandCount; s++)
        {
            var points = new WavePoint[_samplesPerStrand];
            var phase = s * (Math.PI / _strandCount);        // fan the strands apart
            var strandAmp = amp * (0.6 + 0.4 * ((s + 1.0) / _strandCount)); // inner strands smaller
            var freq = 1.5 + s * 0.35;                        // subtly different wavelengths
            for (var i = 0; i < _samplesPerStrand; i++)
            {
                var t = i / (double)(_samplesPerStrand - 1);  // 0..1 across width
                var x = t * width;
                // Envelope tapers amplitude to zero at both ends so strands meet at the caps.
                var envelope = Math.Sin(Math.PI * t);
                var y = centerY + strandAmp * envelope * Math.Sin(2 * Math.PI * freq * t + phase + timeSeconds * 3.0);
                points[i] = new WavePoint(x, y);
            }
            strands[s] = points;
        }
        return strands;
    }
}
