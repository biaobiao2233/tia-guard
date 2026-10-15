using TwinCat3.Engineering;
using Xunit;

namespace TwinCat3.Engineering.Tests;

public sealed class ExecutionGuardTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "tc55-pure-" + Guid.NewGuid().ToString("N"));
    public ExecutionGuardTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact] public void DefaultAuthorityCannotIssueOrConsume()
    {
        var authority = new ExecutionGrantStore();
        Assert.Throws<InvalidOperationException>(() => authority.Issue("session", Hash));
        Assert.Throws<InvalidOperationException>(() => authority.Consume(Hash, "session", Hash));
    }
    [Fact] public void GrantCanOnlyBeConsumedOnce()
    {
        var authority = new ExecutionGrantStore(true);
        string token = authority.Issue("session", Hash);
        authority.Consume(token, "session", Hash);
        Assert.Throws<InvalidOperationException>(() => authority.Consume(token, "session", Hash));
    }
    [Fact] public void WrongSessionConsumesAndRejectsGrant()
    {
        var authority = new ExecutionGrantStore(true);
        string token = authority.Issue("session", Hash);
        Assert.Throws<InvalidOperationException>(() => authority.Consume(token, "other", Hash));
        Assert.Throws<InvalidOperationException>(() => authority.Consume(token, "session", Hash));
    }
    [Fact] public void HashDriftRejectsGrant()
    {
        var authority = new ExecutionGrantStore(true);
        string token = authority.Issue("session", Hash);
        Assert.Throws<InvalidOperationException>(() => authority.Consume(token, "session", new string('a', 64)));
    }
    [Fact] public void ExpiredGrantRejected()
    {
        var clock = new TestClock();
        var authority = new ExecutionGrantStore(true, clock);
        string token = authority.Issue("session", Hash, TimeSpan.FromSeconds(1));
        clock.Now = clock.Now.AddSeconds(1);
        Assert.Throws<InvalidOperationException>(() => authority.Consume(token, "session", Hash));
    }
    [Fact] public async Task ParallelReuseOnlyAllowsOne()
    {
        var authority = new ExecutionGrantStore(true);
        string token = authority.Issue("session", Hash);
        int accepted = 0;
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            try { authority.Consume(token, "session", Hash); Interlocked.Increment(ref accepted); }
            catch (InvalidOperationException) { }
        })));
        Assert.Equal(1, accepted);
    }
    [Theory] [InlineData("MAIN")] [InlineData("FB_Motor")] [InlineData("_foo1")]
    public void ValidIecIdentifier(string name) => Assert.Equal(name, EngineeringPaths.RequireIdentifier(name));
    [Theory] [InlineData("../A")] [InlineData("A^MAIN")] [InlineData("X-Y")] [InlineData("CON")]
    [InlineData("PROGRAM")] [InlineData("1Bad")] [InlineData("")]
    public void BadIecIdentifier(string name) => Assert.Throws<ArgumentException>(() => EngineeringPaths.RequireIdentifier(name));
    [Fact] public void OutputCollisionRejected()
    {
        string target = Path.Combine(root, "existing"); Directory.CreateDirectory(target);
        Assert.Throws<IOException>(() => EngineeringPaths.RequireNewOutput(target, root));
    }
    [Fact] public void AllocationRootCannotBeTarget() => Assert.Throws<ArgumentException>(() => EngineeringPaths.RequireUnder(root, root));
    [Fact] public void PrefixSiblingIsOutsideAllocation()
        => Assert.Throws<ArgumentException>(() => EngineeringPaths.RequireUnder(root + "evil/file", root));
    [Fact] public void TreesCannotOverlap()
        => Assert.Throws<ArgumentException>(() => EngineeringPaths.RequireDistinctTrees(root, Path.Combine(root, "child")));
    [Theory] [InlineData("../main.st")] [InlineData("a/../main.st")] [InlineData("a//main.st")]
    [InlineData("D:/main.st")] [InlineData("a\\main.st")]
    public void SourceTraversalRejected(string path) => Assert.Throws<ArgumentException>(() => EngineeringPaths.RequireSourceFile(path, root));

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-10-12T00:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
