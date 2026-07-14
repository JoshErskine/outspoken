using Outspoken.Core.Transcription;

namespace Outspoken.Core.Tests;

public class TranscriptFiltersTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("[BLANK_AUDIO]")]
    [InlineData("[ Silence ]")]
    [InlineData("(silence)")]
    [InlineData("(music playing)")]
    [InlineData("...")]
    [InlineData(".")]
    public void KnownNonSpeech_IsBlank(string transcript)
    {
        Assert.True(TranscriptFilters.IsBlank(transcript));
    }

    [Theory]
    [InlineData("Hello world")]
    [InlineData("sell the shares on Wednesday")]
    [InlineData("open cell B4")]
    [InlineData("It costs $5.")]
    public void RealSpeech_IsNotBlank(string transcript)
    {
        Assert.False(TranscriptFilters.IsBlank(transcript));
    }
}
