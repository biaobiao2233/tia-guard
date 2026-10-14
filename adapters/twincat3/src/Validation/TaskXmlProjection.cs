using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace TiaGuard.TwinCat.Validation;

public sealed record TaskXmlEvidence(long? CycleTimeUs, int? Priority, IReadOnlyList<string> Unknowns)
{
    public bool Complete => CycleTimeUs.HasValue && Priority.HasValue && Unknowns.Count == 0;
}

public static class TaskXmlProjection
{
    private static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>
    {
        ["AmsPort"] = "350", ["AutoStart"] = "true", ["IoAtBegin"] = "false",
        ["ExceedWarning"] = "0", ["CreateSymbols"] = "false", ["ExtSync"] = "false",
        ["TickModulo"] = "0", ["InputUpdatePreTicks"] = "0", ["DisableFpExceptions"] = "false",
        ["WatchdogStackCapacity"] = "0", ["CpuAffinity"] = "#x0"
    };
    // This is deliberately narrow. A newly observed task option needs an independently justified expected default.
    public static TaskXmlEvidence Read(string xml)
    {
        var unknown = new List<string>();
        long? cycle = null; int? priority = null;
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
                { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 20000000 });
            var document = XDocument.Load(reader);
            var root = document.Root ?? throw new InvalidDataException("Missing task XML root.");
            if (root.Name != "TreeItem") unknown.Add("Unknown task XML root or namespace: " + root.Name);
            foreach (var attribute in root.Attributes()) unknown.Add("Unknown task root attribute: " + attribute.Name);
            foreach (var element in root.Elements())
            {
                if (element.Name.LocalName is "ItemName" or "PathName" or "ItemType" or "ItemId" or "ObjectId" or "ItemSubType" or "ItemTypeName" or "TreeImageData16x14" or "TreeImageId" or "ChildCount" or "Disabled")
                {
                    if (element.HasElements || element.HasAttributes || element.Name.NamespaceName != "") unknown.Add("Opaque task identity/layout field: " + element.Name);
                    if (element.Name == "Disabled" && element.Value != "false") unknown.Add("Task is disabled.");
                    if (element.Name == "ChildCount" && element.Value != "0") unknown.Add("Unexpected task children.");
                    continue;
                }
                if (element.Name != "TaskDef" && element.Name != "TcModuleInstance") unknown.Add("Unknown task metadata: " + element.Name);
            }
            var definitions = root.Elements("TaskDef").ToArray();
            if (definitions.Length != 1) throw new InvalidDataException("Missing or duplicate TaskDef.");
            var definition = definitions[0];
            foreach (var attribute in definition.Attributes()) unknown.Add("Unknown TaskDef attribute: " + attribute.Name);
            foreach (var field in definition.Elements())
            {
                if (field.HasElements || field.HasAttributes || field.Name.NamespaceName != "") unknown.Add("Opaque TaskDef field: " + field.Name);
                if (field.Name != "CycleTime" && field.Name != "Priority" &&
                    (!Defaults.TryGetValue(field.Name.LocalName, out var expected) || field.Value != expected))
                    unknown.Add("Unproven TaskDef option/default: " + field.Name);
            }
            foreach (var group in definition.Elements().GroupBy(e => e.Name))
                if (group.Count() != 1) unknown.Add("Duplicate TaskDef property: " + group.Key);
            var ticks = long.Parse(Required("CycleTime"), CultureInfo.InvariantCulture);
            if (ticks <= 0 || ticks % 10 != 0) unknown.Add("CycleTime cannot be represented exactly in positive microseconds.");
            else cycle = ticks / 10;
            priority = int.Parse(Required("Priority"), CultureInfo.InvariantCulture);
            if (priority < 1 || priority > 255) unknown.Add("Invalid Task priority.");
            var modules = root.Elements("TcModuleInstance").ToArray();
            if (modules.Length > 1) unknown.Add("Duplicate task TcModuleInstance.");
            if (modules.Length == 1) ValidateModule(root, modules[0], ticks, priority.Value, unknown);
            string Required(string name)
            {
                var values = definition.Elements(name).ToArray();
                if (values.Length != 1) throw new InvalidDataException("Missing or duplicate task property: " + name);
                return values[0].Value;
            }
        }
        catch (Exception ex) { unknown.Add("Task XML capture projection failed: " + ex.Message); }
        return new(cycle, priority, unknown);
    }

    private static void ValidateModule(XElement root, XElement module, long ticks, int priority, List<string> unknown)
    {
        // Native 4026.27 ADT metadata: verify every field against the standard task shape,
        // linking dynamic identity to the tree and timing to independently captured TaskDef.
        var nsCycle = checked(ticks * 100);
        var expected = XElement.Parse("<TcModuleInstance><DataTypes/><Module><Name>ADT</Name>" +
            "<CLSID ClassFactory='TcRtsObjects'>{01020001-0000-0000-F000-000000000064}</CLSID><InitSequence>PO</InitSequence>" +
            "<Contexts><Context><Id>1</Id><Name/><Priority/><CycleTime/></Context></Contexts>" +
            "<Interfaces><Interface><IID>{02000002-0000-0000-E000-000000000064}</IID><Type>ITcTask</Type></Interface>" +
            "<Interface><IID>{02000003-0000-0000-E000-000000000064}</IID><Type>ITcRTimeTask</Type></Interface>" +
            "<Interface><IID>{0300001E-0000-0000-E000-000000000064}</IID><Type>ITcCyclicCaller</Type></Interface>" +
            "<Interface><IID>{0300001F-0000-0000-E000-000000000064}</IID><Type>ITcIoCyclicCaller</Type></Interface></Interfaces>" +
            "<Parameters><Parameter><Name>CycleTime</Name><BitSize>32</BitSize><BaseType>UDINT</BaseType><PTCID>#x02001001</PTCID></Parameter>" +
            "<Parameter><Name>Priority</Name><BitSize>32</BitSize><BaseType>UDINT</BaseType><PTCID>#x02001002</PTCID><PTCID>#x01020001</PTCID></Parameter></Parameters>" +
            "<InstanceName/><OTCID/><ParameterValues><Value><Name>CycleTime</Name><Value/></Value><Value><Name>Priority</Name><Value/></Value></ParameterValues></Module></TcModuleInstance>");
        var body = expected.Element("Module")!;
        var context = body.Element("Contexts")!.Element("Context")!;
        var name = root.Element("ItemName")?.Value ?? throw new InvalidDataException("Native task name is missing.");
        var id = root.Element("ObjectId")?.Value ?? throw new InvalidDataException("Native task object ID is missing.");
        context.Element("Name")!.Value = body.Element("InstanceName")!.Value = name;
        body.Element("OTCID")!.Value = id;
        context.Element("Priority")!.Value = priority.ToString(CultureInfo.InvariantCulture);
        context.Element("CycleTime")!.Value = nsCycle.ToString(CultureInfo.InvariantCulture);
        var values = body.Element("ParameterValues")!.Elements("Value").ToArray();
        values[0].Element("Value")!.Value = "#x" + nsCycle.ToString("x", CultureInfo.InvariantCulture);
        values[1].Element("Value")!.Value = "#x" + priority.ToString("x", CultureInfo.InvariantCulture);
        if (!XNode.DeepEquals(Canonical(expected), Canonical(module)))
        {
            unknown.Add("Unexpected task TcModuleInstance configuration or timing." +
                DescribeDifference(Canonical(expected), Canonical(module), "TcModuleInstance"));
        }
    }

    private static XElement Canonical(XElement element)
        => new(element.Name, element.Attributes().OrderBy(a => a.Name.ToString(), StringComparer.Ordinal)
            .Select(a => new XAttribute(a.Name, a.Value)),
            element.Nodes().Where(n => n is not XText text ||
                (text.Value.Length > 0 && (!element.HasElements || !string.IsNullOrWhiteSpace(text.Value))))
                .Select(n => n is XElement child ? Canonical(child) : n));

    private static string DescribeDifference(XElement expected, XElement actual, string path)
    {
        if (expected.Name != actual.Name) return " Different element at " + path + ": " + actual.Name;
        var expectedAttributes = string.Join(";", expected.Attributes().Select(a => a.Name + "=" + a.Value));
        var actualAttributes = string.Join(";", actual.Attributes().Select(a => a.Name + "=" + a.Value));
        if (expectedAttributes != actualAttributes) return " Different attributes at " + path + ": expected " + expectedAttributes + "; captured " + actualAttributes;
        var left = expected.Elements().ToArray(); var right = actual.Elements().ToArray();
        if (left.Length != right.Length) return " Different child count at " + path;
        if (left.Length == 0 && expected.Value != actual.Value) return " Different value at " + path;
        for (var i = 0; i < left.Length; i++)
            if (!XNode.DeepEquals(left[i], right[i])) return DescribeDifference(left[i], right[i], path + "/" + left[i].Name + "[" + i + "]");
        return " Different XML nodes at " + path;
    }
}
