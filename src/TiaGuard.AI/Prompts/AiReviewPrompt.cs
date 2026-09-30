namespace TiaGuard.AI.Prompts;

/// <summary>
/// Provider-neutral prompt content. Provider adapters should pass these values as separate
/// system and user messages where their API supports message roles.
/// </summary>
public sealed record AiReviewPrompt(string SystemInstructions, string UserPayloadJson);
