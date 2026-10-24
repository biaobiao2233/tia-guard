using TiaGuard.AI.Contracts;
using TiaGuard.AI.Prompts;
using TiaGuard.AI.Providers;

namespace TiaGuard.AI.Services;

/// <summary>
/// Orchestrates prompt creation, provider invocation, and advisory result labeling.
/// It has no dependency on or mutation path to deterministic analysis findings.
/// </summary>
public sealed class AiReviewService
{
    private readonly IAiReviewProvider _provider;
    private readonly SnapshotPromptBuilder _promptBuilder;

    public AiReviewService(IAiReviewProvider provider, SnapshotPromptBuilder? promptBuilder = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _promptBuilder = promptBuilder ?? new SnapshotPromptBuilder();
    }

    public static AiReviewService CreateWithFakeProvider() => new(new FakeAiReviewProvider());

    public async Task<AiEngineeringReview> ReviewAsync(
        SnapshotV1 snapshot,
        IEnumerable<BlockSourceText>? blockSources = null,
        CancellationToken cancellationToken = default)
    {
        var prompt = _promptBuilder.Build(snapshot, blockSources);
        cancellationToken.ThrowIfCancellationRequested();

        var draft = await _provider.ReviewAsync(prompt, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The AI provider returned no review draft.");

        ArgumentNullException.ThrowIfNull(draft.Summary);
        ArgumentNullException.ThrowIfNull(draft.Suggestions);
        ArgumentNullException.ThrowIfNull(draft.Questions);

        return new AiEngineeringReview(
            AiReviewStatus.Advisory,
            _provider.Name,
            draft.Summary,
            draft.Suggestions.ToArray(),
            draft.Questions.ToArray(),
            DateTimeOffset.UtcNow);
    }
}
