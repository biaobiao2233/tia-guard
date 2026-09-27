using System.Text.Json.Serialization;

namespace TiaGuard.AI.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<ReviewSeverity>))]
public enum ReviewSeverity
{
    Informational,
    Low,
    Medium,
    High
}

[JsonConverter(typeof(JsonStringEnumConverter<AiReviewStatus>))]
public enum AiReviewStatus
{
    Advisory
}

/// <summary>
/// A suggestion produced by an AI reviewer. It is not a deterministic rule finding.
/// </summary>
public sealed record AiReviewSuggestion(
    string Title,
    string Rationale,
    string Recommendation,
    ReviewSeverity Severity,
    IReadOnlyList<string> Evidence);

/// <summary>
/// Structured, advisory-only output from an AI review provider.
/// Deterministic findings are intentionally represented by a separate subsystem and are
/// neither accepted nor replaced by this model.
/// </summary>
public sealed record AiEngineeringReview(
    AiReviewStatus Status,
    string Provider,
    string Summary,
    IReadOnlyList<AiReviewSuggestion> Suggestions,
    IReadOnlyList<string> Questions,
    DateTimeOffset CreatedAtUtc);

/// <summary>
/// Provider-neutral structured content returned by an AI provider adapter.
/// </summary>
public sealed record AiReviewDraft(
    string Summary,
    IReadOnlyList<AiReviewSuggestion> Suggestions,
    IReadOnlyList<string> Questions);
