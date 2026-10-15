using System.Text.Json;
using TwinCat3.Runtime;
using Xunit;

namespace TwinCat3.WorkerTests;

// These tests qualify boundaries only; they never simulate ADS or claim PLC execution.
public sealed class SimulationPolicyTests
{
    [Theory][InlineData("empty")][InlineData("attribute")][InlineData("device")][InlineData("foreign-axis")][InlineData("missing-motion")]
    public void NcTargetEmptyIoMarkerDoesNotPermitDevicesOrMismatchedProfiles(string mutation)
    {
        var document = System.Xml.Linq.XDocument.Load(Path.Combine(AppContext.BaseDirectory, "fixtures", "nc-4026.27.xml"));
        var motion = TwinCat3.Contracts.MotionConfiguration.Capture(document, "PLC")!;
        var io = new System.Xml.Linq.XElement("Io");
        document.Root!.Element("Project")!.Add(io);
        if (mutation == "attribute") io.SetAttributeValue("DeviceId", 1);
        if (mutation == "device") io.Add(new System.Xml.Linq.XElement("Device"));
        if (mutation == "foreign-axis") motion = motion with { AxisName = "OtherAxis" };
        if (mutation == "missing-motion") motion = null;
        var file = Path.Combine(Path.GetTempPath(), "tc-nc-policy-" + Guid.NewGuid().ToString("N") + ".tsproj");
        try
        {
            document.Save(file);
            if (mutation == "empty") SimulationPolicy.RequireNoHardware(file, motion, "PLC");
            else Assert.ThrowsAny<Exception>(() => SimulationPolicy.RequireNoHardware(file, motion, "PLC"));
        }
        finally { File.Delete(file); }
    }
    [Fact] public void TwinCatGeneratedApplicationNameIsDifferentFromProjectName()
        => SimulationPolicy.RequireApplicationIdentity("Port_851", "MotorPLC", 851, "MotorPLC");

    [Theory]
    [InlineData("MotorPLC", "MotorPLC", 851)]
    [InlineData("Port_851", "OtherPLC", 851)]
    [InlineData("Port_852", "MotorPLC", 852)]
    [InlineData("Port_851", "Port_851", 851)]
    public void RefuseWrongApplicationProjectOrPort(string application, string project, int port)
        => Assert.Throws<InvalidOperationException>(() => SimulationPolicy.RequireApplicationIdentity(application, project, port, "MotorPLC"));
    [Fact] public void SimulationUsesNewScratchOutputWithoutWeakeningPublicationRules()
    {
        var root = Path.Combine(Path.GetTempPath(), "tc-runtime-path-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "input"); var scratch = Path.Combine(root, "scratch");
        Directory.CreateDirectory(source); Directory.CreateDirectory(scratch);
        try
        {
            var output = Path.Combine(scratch, "simulation");
            Assert.Equal(output, SimulationPolicy.CheckOutput(output, scratch, source));
            Assert.Throws<ArgumentException>(() => new TwinCat3.Engineering.EngineeringAllocation(root, scratch).CheckOutput(output, source));
            Assert.Throws<ArgumentException>(() => SimulationPolicy.CheckOutput(Path.Combine(root, "outside"), scratch, source));
            Directory.CreateDirectory(output);
            Assert.Throws<IOException>(() => SimulationPolicy.CheckOutput(output, scratch, source));
        }
        finally { Directory.Delete(root, true); }
    }
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
