using System.Text.Json;
using TwinCat3.Runtime;
using Xunit;

namespace TwinCat3.WorkerTests;

// These tests qualify boundaries only; they never simulate ADS or claim PLC execution.
public sealed class SimulationPolicyTests
{
    [Theory]
    [InlineData("1.2.3.4.5.6")]
    [InlineData("127.0.0.1.10.22")]
    [InlineData("127.0.0.1.64.256")]
    [InlineData("127.0.0.1.64.1.extra")]
    [InlineData("127.0.0.1.64.-1")]
    public void RejectForeignOrExistingAddressRanges(string value)
        => Assert.Throws<InvalidDataException>(() => SimulationPolicy.RequireLoopbackAllocation(value));

    [Fact] public void OnlyFreshSimulationAddressRangeIsPermitted()
        => SimulationPolicy.RequireLoopbackAllocation("127.0.0.1.64.255");

    [Theory]
    [InlineData("INT", "32768")]
    [InlineData("UINT", "-1")]
    [InlineData("UDINT", "4294967296")]
    [InlineData("REAL", "1e100")]
    [InlineData("BOOL", "1")]
    [InlineData("POINTER TO BOOL", "true")]
    public void RejectOverflowNonfiniteAndMismatchedValues(string type, string json)
    {
        using var value = JsonDocument.Parse(json);
        Assert.ThrowsAny<Exception>(() => SimulationPolicy.ScalarValue(type, value.RootElement));
    }

    [Theory]
    [InlineData("<Io />")]
    [InlineData("<System><Device /></System>")]
    [InlineData("<Motion />")]
    [InlineData("<System><EtherCAT /></System>")]
    public void RejectHardwareAndUnknownConfigurationSections(string sections)
    {
        var file = Path.Combine(Path.GetTempPath(), "tc-runtime-policy-" + Guid.NewGuid().ToString("N") + ".tsproj");
        try
        {
            File.WriteAllText(file, "<TcSmProject><Project>" + sections + "</Project></TcSmProject>");
            Assert.Throws<NotSupportedException>(() => SimulationPolicy.RequireNoHardware(file));
        }
        finally { File.Delete(file); }
    }
}
