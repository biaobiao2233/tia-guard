# TiaGuard.AI

Provider-neutral, advisory engineering review contracts for Snapshot v1.

## Use

Pass an `IAiReviewProvider` adapter to `AiReviewService`. The repository includes
`FakeAiReviewProvider` for local composition checks and tests; it makes no network calls.
No OpenAI, Claude, Gemini, or other provider SDK is referenced by this project.

```csharp
string snapshotJson = File.ReadAllText("snapshot.json");
SnapshotV1 snapshot = SnapshotV1Json.Deserialize(snapshotJson);
var service = AiReviewService.CreateWithFakeProvider();
AiEngineeringReview review = await service.ReviewAsync(snapshot);
// review.Status is always Advisory. Keep it separate from deterministic findings.
```

Supply optional exported source with `await service.ReviewAsync(snapshot, blockSources)`;
each item is a `BlockSourceText` value.

Provider adapters receive `AiReviewPrompt`, which keeps system instructions and the JSON
user payload separate. A future adapter should map this to its provider's message and
structured-output API, parse a response into `AiReviewDraft`, and avoid logging prompt
content, credentials, or raw provider errors that may contain them.

## Privacy boundary

`SnapshotPromptBuilder` constructs an allowlisted payload from Snapshot v1. It omits
`project.path` and `tia.processId` entirely. It redacts Windows and common local home paths,
email addresses, account or credential assignments, and recognizable API token formats in
all included text. Optional block source goes through the same sanitizer. Callers who know
additional local account or identity strings can supply them through
`PromptPrivacyOptions.AdditionalSensitiveValues`; the library does not inspect the host
account, environment, or filesystem.

Prompt text is still an outbound data boundary: review the sanitized payload before using a
remote provider, and do not include credentials or unrelated personal data in engineering
comments or exported source. Deterministic findings are not passed to the AI provider and
cannot be replaced by the advisory result.

## Build and test

```powershell
dotnet build .\src\TiaGuard.AI\TiaGuard.AI.csproj
dotnet test .\src\TiaGuard.AI\Tests\TiaGuard.AI.Tests.csproj
```
