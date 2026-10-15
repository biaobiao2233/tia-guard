namespace TiaGuard.AI.Providers;

/// <summary>
/// Adapter boundary for any AI service or local model. Implementations receive only the
/// sanitized prompt assembled by <see cref="Prompts.SnapshotPromptBuilder"/>.
/// </summary>
public interface IAiReviewProvider
{
    string Name { get; }

    Task<Contracts.AiReviewDraft> ReviewAsync(
        Prompts.AiReviewPrompt prompt,
        CancellationToken cancellationToken = default);
}
