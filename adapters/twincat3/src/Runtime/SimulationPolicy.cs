using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using TwinCat3.Contracts;
using TwinCat3.Engineering;

namespace TwinCat3.Runtime;

public static class SimulationPolicy
{
    public static void RequireApplicationIdentity(string? application, string? project, int port, string expectedProject)
    {
        // Official _AppInfo.AppName contains the ADS port; ProjectName is the
        // actual PLC project name. They are distinct identities, not aliases.
        if (application != "Port_851" || port != 851 || project != expectedProject)
            throw new InvalidOperationException("Running PLC application/project/port differs from the owned project.");
    }
    public static string CheckOutput(string path, string scratch, string source)
    {
        var output = EngineeringPaths.RequireNewOutput(path, scratch);
        EngineeringPaths.RequireDistinctTrees(output, source); return output;
    }
    public static void RequireLoopbackAllocation(string netId)
    {
        var parts = netId.Split('.');
        if (parts.Length != 6 || !parts.Take(4).SequenceEqual(new[] { "127", "0", "0", "1" }) ||
            !byte.TryParse(parts[4], out var first) || first < 64 || !byte.TryParse(parts[5], out _))
            throw new InvalidDataException("Only freshly allocated simulation AMS identities are allowed.");
    }
    public static void RequireNoHardware(string configurationPath, MotionSpec? motion = null, string? plcName = null)
    {
        using var reader = XmlReader.Create(configurationPath, new XmlReaderSettings
            { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 20000000 });
        var root = XDocument.Load(reader).Root;
        var project = root?.Element("Project") ?? throw new InvalidDataException("Missing native configuration.");
        foreach (var section in project.Elements())
            if (section.Name.LocalName is not ("System" or "Plc" or "TypeSystem" or "LicenseInfo")
                && !(section.Name == "Motion" && motion is not null))
                throw new NotSupportedException("Only PLC-only software simulation is qualified; configuration section: " + section.Name);
        if (project.Descendants().Any(e => e.Name.LocalName is "Device" or "EtherCAT" or "EtherCat" or "Box"))
            throw new NotSupportedException("Hardware configuration is not allowed in this simulation slice.");
        if (motion is not null && (plcName is null || MotionConfiguration.Capture(new XDocument(root!), plcName) != motion))
            throw new InvalidDataException("Only the exact frozen, fully mapped NC simulation profile may run.");
    }
    public static void RequireSymbol(FrozenGenerationInput input, string name, bool writing)
    {
        if (name.Length > 256 || !Regex.IsMatch(name, @"\A[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*){1,15}\z"))
            throw new InvalidDataException("A bounded, explicit symbol path is required.");
        var parts = name.Split('.');
        var artifact = input.Manifest.Artifacts.SingleOrDefault(a => a.Name == parts[0]);
        if (artifact is null || artifact.Kind is not (ArtifactKind.Program or ArtifactKind.GlobalVariableList))
            throw new InvalidDataException("Symbol root is outside the frozen PLC source.");
        if (!writing) return;
        var declaration = input.Files[artifact.DeclarationFile].Text;
        if (artifact.Kind != ArtifactKind.GlobalVariableList || parts.Length != 2 ||
            Regex.IsMatch(declaration, @"\b(CONSTANT|AT)\b", RegexOptions.IgnoreCase) ||
            !Regex.IsMatch(declaration, @"(?m)^\s*" + Regex.Escape(parts[1]) + @"\s*:\s*(BOOL|INT|UINT|DINT|UDINT|REAL|LREAL)\b", RegexOptions.IgnoreCase))
            throw new InvalidDataException("Writes require an explicitly declared scalar GVL variable; outputs and special symbols are protected.");
    }
    public static Type ScalarType(string plcType) => plcType.ToUpperInvariant() switch
    {
        "BOOL" => typeof(bool), "INT" => typeof(short), "UINT" => typeof(ushort),
        "DINT" => typeof(int), "UDINT" => typeof(uint), "REAL" => typeof(float), "LREAL" => typeof(double),
        _ => throw new NotSupportedException("Only BOOL/INT/UINT/DINT/UDINT/REAL/LREAL scalars are supported.")
    };
    public static object ScalarValue(string plcType, JsonElement value) => plcType.ToUpperInvariant() switch
    {
        "BOOL" => value.GetBoolean(), "INT" => value.GetInt16(), "UINT" => value.GetUInt16(),
        "DINT" => value.GetInt32(), "UDINT" => value.GetUInt32(),
        "REAL" => Finite(value.GetSingle()), "LREAL" => Finite(value.GetDouble()),
        _ => throw new NotSupportedException("Unsupported scalar type.")
    };
    private static float Finite(float value) => float.IsFinite(value) ? value : throw new InvalidDataException("Value must be finite.");
    private static double Finite(double value) => double.IsFinite(value) ? value : throw new InvalidDataException("Value must be finite.");
}
