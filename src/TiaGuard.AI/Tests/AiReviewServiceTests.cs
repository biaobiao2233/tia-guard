using System.Text.Json;
using Xunit;
using TiaGuard.AI.Contracts;
using TiaGuard.AI.Prompts;
using TiaGuard.AI.Providers;
using TiaGuard.AI.Services;

namespace TiaGuard.AI.Tests;

public sealed class AiReviewServiceTests
{
    [Fact]
    public async Task ReviewAsync_ReturnsStructuredAdvisoryResult_UsingFakeProviderWithoutNetwork()
    {
        AiReviewPrompt? receivedPrompt = null;
        var suggestion = new AiReviewSuggestion(
            "Consider adding an interlock",
            "The supplied block source does not show a mutually exclusive direction check.",
            "Review the forward and reverse contactor interlock logic.",
            ReviewSeverity.Medium,
            ["PLC_1/Main"]);
        var provider = new FakeAiReviewProvider(prompt =>
        {
            receivedPrompt = prompt;
            return new AiReviewDraft("Review summary", [suggestion], ["Is the interlock implemented in another block?"]);
        });
        var service = new AiReviewService(provider);

        var review = await service.ReviewAsync(CreateSnapshot());

        Assert.Equal(AiReviewStatus.Advisory, review.Status);
        Assert.Equal("fake", review.Provider);
        Assert.Equal("Review summary", review.Summary);
        Assert.Same(suggestion, Assert.Single(review.Suggestions));
        Assert.Single(review.Questions);
        Assert.NotNull(receivedPrompt);
        Assert.Contains("schemaVersion", receivedPrompt!.UserPayloadJson, StringComparison.Ordinal);
        Assert.NotEqual(default, review.CreatedAtUtc);

        var json = JsonSerializer.Serialize(review);
        Assert.Contains("\"Status\":\"Advisory\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Severity\":\"Medium\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateWithFakeProvider_CompletesLocally()
    {
        var review = await AiReviewService.CreateWithFakeProvider().ReviewAsync(CreateSnapshot());

        Assert.Equal(AiReviewStatus.Advisory, review.Status);
        Assert.Equal("fake", review.Provider);
        Assert.Empty(review.Suggestions);
    }

    [Fact]
    public async Task ReviewAsync_PropagatesCancellationToProvider()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = new AiReviewService(new FakeAiReviewProvider());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ReviewAsync(CreateSnapshot(), cancellationToken: cancellation.Token));
    }

    private static SnapshotV1 CreateSnapshot() => new(
        "1.0",
        new SnapshotProject("Demo", null),
        new SnapshotTia("V21", null),
        Array.Empty<SnapshotDevice>(),
        [new SnapshotPlc("PLC_1", Array.Empty<SnapshotBlock>(), Array.Empty<SnapshotTag>(), null)]);
}
