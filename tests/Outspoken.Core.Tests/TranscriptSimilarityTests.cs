using Outspoken.Core.Transcription;

namespace Outspoken.Core.Tests;

public class TranscriptSimilarityTests
{
    [Fact]
    public void IdenticalText_IsPerfectMatch()
    {
        Assert.Equal(1.0, TranscriptSimilarity.Compare("hello world", "hello world"));
    }

    [Fact]
    public void CaseAndPunctuation_AreIgnored()
    {
        Assert.Equal(1.0, TranscriptSimilarity.Compare("Hello, world!", "hello world"));
    }

    [Fact]
    public void OneWordWrongInTen_IsNinetyPercent()
    {
        var expected = "one two three four five six seven eight nine ten";
        var actual = "one two three four five six seven eight nine zebra";
        Assert.Equal(0.9, TranscriptSimilarity.Compare(expected, actual), precision: 5);
    }

    [Fact]
    public void MissingWord_CountsAgainstScore()
    {
        var score = TranscriptSimilarity.Compare("the quick brown fox", "the brown fox");
        Assert.Equal(0.75, score, precision: 5);
    }

    [Fact]
    public void CompletelyDifferent_ScoresNearZero()
    {
        Assert.True(TranscriptSimilarity.Compare("alpha beta gamma", "one two three") <= 0.0 + 1e-9);
    }

    [Fact]
    public void EmptyExpected_MatchesOnlyEmptyActual()
    {
        Assert.Equal(1.0, TranscriptSimilarity.Compare("", ""));
        Assert.Equal(0.0, TranscriptSimilarity.Compare("", "something"));
    }
}
