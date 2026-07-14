using Outspoken.Core.Overlay;

namespace Outspoken.Core.Tests;

public class WaveformModelTests
{
    [Fact]
    public void ComputeStrands_ReturnsRequestedStrandCount_WithSamplesEach()
    {
        var model = new WaveformModel(strandCount: 5, samplesPerStrand: 48);
        var strands = model.ComputeStrands(180, 44, timeSeconds: 0);

        Assert.Equal(5, strands.Count);
        Assert.All(strands, s => Assert.Equal(48, s.Length));
    }

    [Fact]
    public void Strands_SpanTheFullWidth_EndpointsAtCaps()
    {
        var model = new WaveformModel();
        var strands = model.ComputeStrands(180, 44, timeSeconds: 0);

        foreach (var strand in strands)
        {
            Assert.Equal(0, strand[0].X, precision: 6);
            Assert.Equal(180, strand[^1].X, precision: 6);
        }
    }

    [Fact]
    public void EnvelopeTapersToCenterLine_AtBothEnds()
    {
        var model = new WaveformModel();
        model.PushLevel(1f); // loud — max amplitude
        var strands = model.ComputeStrands(180, 44, timeSeconds: 0.3);

        // sin(pi*t) envelope is 0 at t=0 and t=1, so strand ends sit on the center line.
        foreach (var strand in strands)
        {
            Assert.Equal(22, strand[0].Y, precision: 3);
            Assert.Equal(22, strand[^1].Y, precision: 3);
        }
    }

    [Fact]
    public void LouderVoice_ProducesTallerWeave()
    {
        static double PeakDeviation(WaveformModel m)
        {
            var strands = m.ComputeStrands(180, 44, timeSeconds: 0.25);
            var max = 0.0;
            foreach (var strand in strands)
                foreach (var p in strand)
                    max = Math.Max(max, Math.Abs(p.Y - 22));
            return max;
        }

        // True silence vs loud speech. The sqrt gain deliberately boosts audible speech, so
        // "quiet" here is near-zero (silence), not faint speech — silence must stay flat.
        var silence = new WaveformModel();
        for (var i = 0; i < 50; i++) silence.PushLevel(0.0f);

        var loud = new WaveformModel();
        for (var i = 0; i < 50; i++) loud.PushLevel(0.9f);

        Assert.True(PeakDeviation(loud) > PeakDeviation(silence) * 3,
            "Loud speech should weave much taller than silence.");
    }

    [Fact]
    public void PushLevel_SmoothsTowardTarget_NeverSnaps()
    {
        var model = new WaveformModel(smoothing: 0.25);
        var first = model.PushLevel(1f);

        Assert.True(first < 0.5, "One step of 0.25 smoothing shouldn't reach the target.");
        for (var i = 0; i < 40; i++) model.PushLevel(1f);
        Assert.True(model.Level > 0.95, "Sustained loud input should approach 1.");
    }

    [Fact]
    public void Settle_CollapsesLevelTowardZero()
    {
        var model = new WaveformModel();
        for (var i = 0; i < 40; i++) model.PushLevel(1f);
        Assert.True(model.Level > 0.9);

        for (var i = 0; i < 60; i++) model.Settle();
        Assert.True(model.Level < 0.05, "Settling should flatten the weave.");
    }

    [Fact]
    public void AmplitudeScaleZero_ProducesFlatLine()
    {
        var model = new WaveformModel();
        model.PushLevel(1f);
        var strands = model.ComputeStrands(180, 44, timeSeconds: 0.5, amplitudeScale: 0);

        foreach (var strand in strands)
            Assert.All(strand, p => Assert.Equal(22, p.Y, precision: 6)); // Done state: flat
    }

    [Fact]
    public void StrandCount_OutOfRange_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WaveformModel(strandCount: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WaveformModel(strandCount: 9));
    }
}
