using TiaGuard.AI.Contracts;
using TiaGuard.AI.Prompts;

namespace TiaGuard.AI.Providers;

/// <summary>
/// A local, network-free provider useful for tests and end-to-end composition checks.
/// </summary>
public sealed class FakeAiReviewProvider : IAiReviewProvider
{
    private readonly Func<AiReviewPrompt, AiReviewDraft> _createDraft;

    public FakeAiReviewProvider(Func<AiReviewPrompt, AiReviewDraft>? createDraft = null)
    {
        _createDraft = createDraft ?? (_ => new AiReviewDraft(
            "Fake provider completed a local review.",
            Array.Empty<AiReviewSuggestion>(),
            Array.Empty<string>()));
    }

    public string Name => "fake";

    public Task<AiReviewDraft> ReviewAsync(
        AiReviewPrompt prompt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_createDraft(prompt));
    }
}
