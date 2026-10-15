using System.Xml.Linq;
using C = TwinCat3.Contracts;
using Xunit;

namespace TiaGuard.TwinCat.Validation.Tests;

public sealed class MotionTests
{
    public static C.MotionSpec Spec => new()
    {
        TaskName = "DemoNC", AxisName = "Axis1", AxisSymbol = "GVL_Axis.Axis", CycleTimeUs = 2000,
        MinimumPosition = 0, MaximumPosition = 100, MaximumVelocity = 100, Acceleration = 240, Deceleration = 240, Jerk = 1200
    };
    // Minimal independent saved configuration, including localized process-image group names.
    public static XDocument Configuration() => XDocument.Parse("""
        <TcSmProject><Project><Motion><NC>
        <SafTask Priority="4" AmsPort="501" CycleTime="20000"><Name>DemoNC SAF</Name></SafTask>
        <SvbTask Priority="8" AmsPort="511" CycleTime="100000"><Name>DemoNC SVB</Name></SvbTask>
        <Axis Id="1" AxisType="1"><Name>Axis1</Name>
        <AxisPara><Dynamic Acceleration="240" Deceleration="240" Jerk="1200"/><Velo Maximum="100"/></AxisPara>
        <Encoder EncType="1"><EncPara><SoftEndMinControl Enable="true" Range="0"/><SoftEndMaxControl Enable="true" Range="100"/></EncPara></Encoder>
        <Drive DrvType="5"/>
        <Vars VarGrpType="1"><Name>输入</Name><Var><Name>FromPlc</Name><Type>PLCTONC_AXIS_REF</Type></Var></Vars>
        <Vars VarGrpType="2"><Name>输出</Name><Var><Name>ToPlc</Name><Type>NCTOPLC_AXIS_REF</Type></Var></Vars>
        </Axis></NC></Motion><Plc><Project Name="PLC"><Instance><Name>PLC Instance</Name>
        <Vars VarGrpType="1"><Name>MotionTask Inputs</Name><Var><Name>GVL_Axis.Axis.NcToPlc</Name><Type>NCTOPLC_AXIS_REF</Type></Var></Vars>
        <Vars VarGrpType="2"><Name>MotionTask Outputs</Name><Var><Name>GVL_Axis.Axis.PlcToNc</Name><Type>PLCTONC_AXIS_REF</Type></Var></Vars>
        </Instance></Project></Plc></Project>
        <Mappings><OwnerA Name="TIPC^PLC^PLC Instance"><OwnerB Name="TINC^DemoNC SAF^Axes^Axis1">
        <Link VarA="MotionTask Inputs^GVL_Axis.Axis.NcToPlc" VarB="输出^ToPlc"/>
        <Link VarA="MotionTask Outputs^GVL_Axis.Axis.PlcToNc" VarB="输入^FromPlc"/>
        </OwnerB></OwnerA></Mappings></TcSmProject>
        """);

    private static C.GenerationManifest Manifest(C.MotionSpec spec) => new()
    {
        SchemaVersion = "1.0", SolutionName = "Motion", ConfigurationName = "Configuration", PlcProjectName = "PLC",
        Environment = new(), Motion = spec,
        Artifacts = new[] {
            new C.ArtifactSpec { Id = "main", Name = "MAIN", Kind = C.ArtifactKind.Program, DeclarationFile = "main.st", ImplementationFile = "impl.st" },
            new C.ArtifactSpec { Id = "axis", Name = "GVL_Axis", Kind = C.ArtifactKind.GlobalVariableList, DeclarationFile = "axis.st" } },
        Tasks = new[] { new C.TaskSpec { Name = "PlcTask", CycleTimeUs = 2000, Priority = 20, Programs = new[] { "MAIN" } } },
        Libraries = new[] { new C.LibrarySpec { Name = "Tc2_MC2", Version = "3.3.75.0", Vendor = "Beckhoff Automation GmbH" } }
    };
    [Fact] public void ManifestAcceptsBoundedSynchronizedVirtualAxis() => C.GenerationInputLoader.ValidateManifest(Manifest(Spec));
    [Theory][InlineData("cycle")][InlineData("symbol")][InlineData("range")][InlineData("velocity")][InlineData("nan")]
    public void ManifestRejectsUnboundedOrAmbiguousMotion(string mutation)
    {
        var invalid = mutation switch
        {
            "cycle" => Spec with { CycleTimeUs = 1000 }, "symbol" => Spec with { AxisSymbol = "GVL_Axis.Axis.Raw" },
            "range" => Spec with { MaximumPosition = -1 }, "velocity" => Spec with { MaximumVelocity = 0 },
            _ => Spec with { Acceleration = double.NaN }
        };
        Assert.ThrowsAny<Exception>(() => C.GenerationInputLoader.ValidateManifest(Manifest(invalid)));
    }

    [Fact] public void IndependentSavedParametersAndLocalizedLinksAreCaptured()
        => Assert.Equal(Spec, C.MotionConfiguration.Capture(Configuration(), "PLC"));
    [Fact] public void ActualDynamicsAreNeverCopiedFromExpected()
    {
        var doc = Configuration(); doc.Descendants("Velo").Single().SetAttributeValue("Maximum", "50");
        Assert.Equal(50, C.MotionConfiguration.Capture(doc, "PLC")!.MaximumVelocity);
        Assert.NotEqual(Spec, C.MotionConfiguration.Capture(doc, "PLC"));
    }
    [Theory]
    [InlineData("missing-link")][InlineData("offset-link")][InlineData("foreign-owner")][InlineData("extra-axis")]
    [InlineData("real-encoder")][InlineData("real-drive")][InlineData("disabled-limit")][InlineData("io-binding")]
    [InlineData("wrong-type")][InlineData("nc-i")]
    public void IncompleteOrForeignMotionCannotBeQualified(string mutation)
    {
        var doc = Configuration();
        switch (mutation)
        {
            case "missing-link": doc.Descendants("Link").First().Remove(); break;
            case "offset-link": doc.Descendants("Link").First().SetAttributeValue("OffsA", 8); break;
            case "foreign-owner": doc.Descendants("OwnerB").Single().SetAttributeValue("Name", "TIID^Drive"); break;
            case "extra-axis": doc.Descendants("NC").Single().Add(new XElement(doc.Descendants("Axis").Single())); break;
            case "real-encoder": doc.Descendants("Encoder").Single().SetAttributeValue("EncType", 7); break;
            case "real-drive": doc.Descendants("Drive").Single().SetAttributeValue("DrvType", 42); break;
            case "disabled-limit": doc.Descendants("SoftEndMaxControl").Single().SetAttributeValue("Enable", false); break;
            case "io-binding": doc.Descendants("Axis").Single().Add(new XElement("IoItem", "TIID^Drive")); break;
            case "wrong-type": doc.Descendants("Type").First().Value = "BOOL"; break;
            case "nc-i": doc.Descendants("NC").Single().Add(new XElement("Channel")); break;
        }
        Assert.ThrowsAny<Exception>(() => C.MotionConfiguration.Capture(doc, "PLC"));
    }
    [Fact] public void AxisParameterXmlIsInvariantAndHasNoPhysicalEndpoints()
    {
        using var culture = new CultureScope("de-DE");
        var xml = XDocument.Parse(C.MotionConfiguration.AxisXml(Spec with { Acceleration = 240.5 }));
        Assert.Equal("240.5", xml.Descendants("Acceleration").Single().Value);
        Assert.Empty(xml.Descendants("IoItem"));
    }
    private sealed class CultureScope : IDisposable
    {
        private readonly System.Globalization.CultureInfo before = System.Globalization.CultureInfo.CurrentCulture;
        public CultureScope(string name) => System.Globalization.CultureInfo.CurrentCulture = new(name);
        public void Dispose() => System.Globalization.CultureInfo.CurrentCulture = before;
    }
}
