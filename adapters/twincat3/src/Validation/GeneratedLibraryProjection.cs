using System.Xml;
using System.Xml.Linq;

namespace TiaGuard.TwinCat.Validation;

public static class GeneratedLibraryProjection
{
    public static bool IsSystemGlobalTypes(string xml)
    {
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
                { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 20000000 });
            var document = XDocument.Load(reader);
            var matches = document.Root?.Element("PlcLibDef")?.Element("References")?.Elements("PlaceholderReference")
                .Where(e => e.Element("LibItemName")?.Value == "#Tc3_GlobalTypes").ToArray();
            if (matches?.Length != 1) return false;
            var item = matches[0];
            var resolved = item.Element("EffectiveResolution");
            return item.Attribute("systemLibrary")?.Value == "true" && item.Element("PlaceholderName")?.Value == "Tc3_GlobalTypes"
                && item.Element("Namespace")?.Value == "Tc3_GlobalTypes" && resolved?.Element("LibraryName")?.Value == "Tc3_GlobalTypes"
                && resolved.Element("Version")?.Value == "1.0.0.0" && resolved.Element("Distributor")?.Value == "Beckhoff Automation GmbH";
        }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException) { return false; }
    }
}
