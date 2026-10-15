namespace TiaGuard.AI.Privacy;

/// <summary>
/// Additional caller-known identity strings to remove before prompt data is sent to a
/// provider. The library never reads the local account name or environment variables.
/// </summary>
public sealed record PromptPrivacyOptions(IReadOnlyList<string>? AdditionalSensitiveValues = null);
