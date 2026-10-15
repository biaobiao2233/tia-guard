using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace TwinCat3.Contracts;

/// <summary>Projection of saved native XML, not a copy of the requested motion specification.</summary>
public static class MotionConfiguration
{
    public sealed record Endpoint(string PlcOwner, string NcOwner, string PlcVariable, string NcVariable);

    public static XDocument Read(string path)
    {
        using var reader = XmlReader.Create(path, new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 20000000 });
        return XDocument.Load(reader);
    }

    public static string AxisXml(MotionSpec spec)
    {
        static string N(double v) => v.ToString("R", CultureInfo.InvariantCulture);
        return new XElement("TreeItem", new XElement("NcAxisDef",
            new XElement("AxisType", 1),
            new XElement("AxisParam", new XElement("Unit", "mm"),
                new XElement("Dynamic", new XElement("Acceleration", N(spec.Acceleration)),
                    new XElement("Deceleration", N(spec.Deceleration)), new XElement("Jerk", N(spec.Jerk))),
                new XElement("Velo", new XElement("Fast", N(spec.MaximumVelocity)), new XElement("Maximum", N(spec.MaximumVelocity))),
                new XElement("TargetPosControl", new XElement("Enable", true), new XElement("Range", "0.01"))),
            new XElement("Encoder", new XElement("EncType", 1), new XElement("EncParam",
                new XElement("SoftEndMinControl", new XElement("Enable", true), new XElement("Range", N(spec.MinimumPosition))),
                new XElement("SoftEndMaxControl", new XElement("Enable", true), new XElement("Range", N(spec.MaximumPosition))))),
            new XElement("Drive", new XElement("DrvType", 5)))).ToString(SaveOptions.DisableFormatting);
    }

    public static IReadOnlyList<Endpoint> Endpoints(XDocument document, string plcName, string axisSymbol)
    {
        var project = document.Root?.Element("Project") ?? throw new InvalidDataException("Missing Project.");
        var nc = project.Element("Motion")?.Element("NC") ?? throw new InvalidDataException("Missing NC.");
        var axis = nc.Elements("Axis").Single();
        string ncOwner = "TINC^" + Text(nc.Element("SafTask"), "Name") + "^Axes^" + Text(axis, "Name");
        var plc = project.Element("Plc")?.Elements().Single(x => (string?)x.Attribute("Name") == plcName)
            ?? throw new InvalidDataException("Missing intended PLC.");
        var instance = plc.Elements("Instance").Single();
        string plcOwner = "TIPC^" + plcName + "^" + Text(instance, "Name");
        return new[] { Find(1, 2, "NcToPlc", "ToPlc", "NCTOPLC_AXIS_REF"),
            Find(2, 1, "PlcToNc", "FromPlc", "PLCTONC_AXIS_REF") };

        Endpoint Find(int plcDirection, int ncDirection, string suffix, string ncName, string type)
        {
            string variable = axisSymbol + "." + suffix;
            var pg = instance.Elements("Vars").Single(g => (int?)g.Attribute("VarGrpType") == plcDirection
                && g.Elements("Var").Any(v => (string?)v.Element("Name") == variable));
            var pv = pg.Elements("Var").Single(v => (string?)v.Element("Name") == variable);
            var ng = axis.Elements("Vars").Single(g => (int?)g.Attribute("VarGrpType") == ncDirection);
            var nv = ng.Elements("Var").Single(v => (string?)v.Element("Name") == ncName);
            if ((string?)pv.Element("Type") != type || (string?)nv.Element("Type") != type)
                throw new InvalidDataException("NC/PLC process-image types differ from AXIS_REF.");
            return new(plcOwner, ncOwner, Text(pg, "Name") + "^" + variable, Text(ng, "Name") + "^" + ncName);
        }
    }

    public static MotionSpec? Capture(XDocument document, string plcName)
    {
        var project = document.Root?.Element("Project") ?? throw new InvalidDataException("Missing Project.");
        var motion = project.Element("Motion");
        if (motion is null) return null;
        if (motion.Elements().Count() != 1 || motion.Elements().Single().Name != "NC")
            throw new NotSupportedException("Only one NC/PTP configuration is supported.");
        var nc = motion.Element("NC")!;
        if (nc.Elements().Any(e => e.Name.LocalName is not ("SafTask" or "SvbTask" or "Axis"))
            || nc.Elements("SafTask").Count() != 1 || nc.Elements("SvbTask").Count() != 1 || nc.Elements("Axis").Count() != 1)
            throw new NotSupportedException("Expected exactly one SAF/SVB pair and one simulation axis.");
        var saf = nc.Element("SafTask")!; var svb = nc.Element("SvbTask")!; var axis = nc.Element("Axis")!;
        string task = Text(saf, "Name");
        if (!task.EndsWith(" SAF", StringComparison.Ordinal) || Text(svb, "Name") != task[..^4] + " SVB"
            || (int?)saf.Attribute("Priority") != 4 || (int?)svb.Attribute("Priority") != 8
            || (int?)saf.Attribute("AmsPort") != 501 || (int?)svb.Attribute("AmsPort") != 511
            || (int?)axis.Attribute("Id") != 1 || (int?)axis.Attribute("AxisType") != 1
            || (int?)axis.Element("Encoder")?.Attribute("EncType") != 1
            || (int?)axis.Element("Drive")?.Attribute("DrvType") != 5
            || axis.Descendants().Any(e => e.Name.LocalName.Contains("IoItem", StringComparison.OrdinalIgnoreCase)))
            throw new NotSupportedException("NC task/axis is outside the continuous software simulation profile.");
        var enc = axis.Element("Encoder")?.Element("EncPara");
        if ((bool?)enc?.Element("SoftEndMinControl")?.Attribute("Enable") != true
            || (bool?)enc?.Element("SoftEndMaxControl")?.Attribute("Enable") != true)
            throw new InvalidDataException("NC software limits must both be enabled.");
        var allLinks = document.Root!.Element("Mappings")?.Descendants("Link").ToArray() ?? Array.Empty<XElement>();
        if (allLinks.Length != 2) throw new InvalidDataException("Exactly two complete PLC/NC mappings are required.");
        var symbols = allLinks.Select(l => (string?)l.Attribute("VarA")).Where(s => s?.EndsWith(".NcToPlc", StringComparison.Ordinal) == true).ToArray();
        if (symbols.Length != 1) throw new InvalidDataException("Missing PLC NC input mapping.");
        string symbol = symbols[0]!.Split('^').Last()[..^8];
        foreach (var endpoint in Endpoints(document, plcName, symbol))
        {
            var link = allLinks.Single(l => (string?)l.Parent?.Parent?.Attribute("Name") == endpoint.PlcOwner
                && (string?)l.Parent?.Attribute("Name") == endpoint.NcOwner
                && (string?)l.Attribute("VarA") == endpoint.PlcVariable && (string?)l.Attribute("VarB") == endpoint.NcVariable);
            if (link.Attributes().Any(a => a.Name.LocalName is not ("VarA" or "VarB")) || link.HasElements)
                throw new InvalidDataException("Partial/offset NC mappings are unsupported.");
        }
        var para = axis.Element("AxisPara");
        return new MotionSpec
        {
            TaskName = task[..^4], AxisName = Text(axis, "Name"), AxisSymbol = symbol,
            CycleTimeUs = checked((long?)saf.Attribute("CycleTime") ?? throw new InvalidDataException("Missing NC cycle.")) / 10,
            MinimumPosition = Number(enc?.Element("SoftEndMinControl"), "Range"),
            MaximumPosition = Number(enc?.Element("SoftEndMaxControl"), "Range"),
            MaximumVelocity = Number(para?.Element("Velo"), "Maximum"),
            Acceleration = Number(para?.Element("Dynamic"), "Acceleration"),
            Deceleration = Number(para?.Element("Dynamic"), "Deceleration"), Jerk = Number(para?.Element("Dynamic"), "Jerk")
        };
    }

    private static string Text(XElement? e, string name) => (string?)e?.Element(name) ?? throw new InvalidDataException("Missing " + name);
    private static double Number(XElement? e, string name)
    {
        if (!double.TryParse((string?)e?.Attribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
            throw new InvalidDataException("Missing/nonfinite NC parameter: " + name);
        return value;
    }
}
