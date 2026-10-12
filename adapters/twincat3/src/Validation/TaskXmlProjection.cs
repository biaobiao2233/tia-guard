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
                if (element.Name.LocalName is "ItemName" or "PathName" or "ItemType" or "ItemId" or "ItemSubType" or "ItemTypeName" or "TreeImageData16x14")
                {
                    if (element.HasElements || element.HasAttributes || element.Name.NamespaceName != "") unknown.Add("Opaque task identity/layout field: " + element.Name);
                    continue;
                }
                if (element.Name != "TaskDef") unknown.Add("Unknown task metadata: " + element.Name);
            }
            var definitions = root.Elements("TaskDef").ToArray();
            if (definitions.Length != 1) throw new InvalidDataException("Missing or duplicate TaskDef.");
            var definition = definitions[0];
            foreach (var attribute in definition.Attributes()) unknown.Add("Unknown TaskDef attribute: " + attribute.Name);
            foreach (var field in definition.Elements())
            {
                if (field.HasElements || field.HasAttributes || field.Name.NamespaceName != "") unknown.Add("Opaque TaskDef field: " + field.Name);
                if (field.Name != "CycleTime" && field.Name != "Priority") unknown.Add("Unproven TaskDef option/default: " + field.Name);
            }
            var ticks = long.Parse(Required("CycleTime"), CultureInfo.InvariantCulture);
            if (ticks <= 0 || ticks % 10 != 0) unknown.Add("CycleTime cannot be represented exactly in positive microseconds.");
            else cycle = ticks / 10;
            priority = int.Parse(Required("Priority"), CultureInfo.InvariantCulture);
            if (priority < 1 || priority > 255) unknown.Add("Invalid Task priority.");
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
}
