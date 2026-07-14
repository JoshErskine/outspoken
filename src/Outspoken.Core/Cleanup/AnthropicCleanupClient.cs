using System.Text;
using Anthropic;
using Anthropic.Models.Messages;

namespace Outspoken.Core.Cleanup;

/// <summary>
/// Cleanup via the Anthropic Messages API (ADR-001: claude-haiku-4-5, the only outbound
/// call in the whole app). Honors the never-block invariant: a 3s timeout, offline, or any
/// API error resolves to the raw transcript rather than throwing (spec §4).
/// </summary>
public sealed class AnthropicCleanupClient : ICleanupClient
{
    private readonly AnthropicClient _client;
    private readonly TimeSpan _timeout;

    public AnthropicCleanupClient(string apiKey, TimeSpan? timeout = null)
    {
        _client = new AnthropicClient { ApiKey = apiKey };
        _timeout = timeout ?? TimeSpan.FromMilliseconds(CoreInfo.CleanupTimeoutMs);
    }

    /// <summary>
    /// Establishes the HTTPS connection and pays the first-call handshake + JIT cost up front,
    /// so the first real dictation isn't ~11s cold (dogfood 2026-07-14 — steady-state is ~0.8s
    /// but call 1 pays DNS+TLS+HTTP/2+JIT). Fire-and-forget at startup; swallows every error —
    /// a failed warm-up just means the first real call is cold, never a crash.
    /// </summary>
    public async Task WarmUpAsync(CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(20)); // generous — cold can be ~11s

        try
        {
            await _client.Messages.Create(new MessageCreateParams
            {
                Model = CoreInfo.CleanupModel,
                MaxTokens = 1, // minimal — we only want the connection hot, not a real answer
                Messages = [new() { Role = Role.User, Content = "hi" }],
            }, cancellationToken: cts.Token);
        }
        catch
        {
            // Best-effort. Offline/slow/auth issues surface on the first real call instead.
        }
    }

    public async Task<CleanupResult> CleanAsync(string rawTranscript, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawTranscript))
            return CleanupResult.Cleaned(rawTranscript);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeout);

        try
        {
            var response = await _client.Messages.Create(new MessageCreateParams
            {
                Model = CoreInfo.CleanupModel,
                MaxTokens = CoreInfo.CleanupMaxTokens,
                System = CleanupContract.SystemPrompt,
                Messages = [new() { Role = Role.User, Content = rawTranscript }],
            }, cancellationToken: timeoutCts.Token);

            var cleaned = ExtractText(response);
            // A model that returns nothing usable is a fallback, not a silent empty paste.
            return string.IsNullOrWhiteSpace(cleaned)
                ? CleanupResult.Raw(rawTranscript, "cleanup returned empty")
                : CleanupResult.Cleaned(cleaned.Trim());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // caller cancelled the whole dictation — not a timeout
        }
        catch (OperationCanceledException)
        {
            return CleanupResult.Raw(rawTranscript, $"cleanup timed out (>{_timeout.TotalMilliseconds:F0}ms)");
        }
        catch (Exception ex)
        {
            // Offline, auth failure, rate limit, 5xx — never block the dictation.
            return CleanupResult.Raw(rawTranscript, $"cleanup failed: {ex.Message}");
        }
    }

    private static string ExtractText(Message response)
    {
        var sb = new StringBuilder();
        foreach (var text in response.Content.Select(b => b.Value).OfType<TextBlock>())
            sb.Append(text.Text);
        return sb.ToString();
    }
}
