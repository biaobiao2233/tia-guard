using System;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace TiaGuard.Openness
{
    public static class ProjectGenerationVerificationXml
    {
        // Extract only the documented root object's interface and instance metadata.
        // This is an observation parser, not an SCL compiler or an XML normalizer.
        public static void ReadInterface(string xml, ProjectGenerationVerificationObject target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            target.InterfaceComplete = false;
            target.InstanceComplete = false;
            target.InterfaceXml = null;
            target.InstanceOfName = null;
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using (var reader = XmlReader.Create(new StringReader(xml), settings))
            {
                var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
                if (document.Root?.Name.LocalName != "Document") throw new InvalidDataException("Not a SimaticML Document.");
                var objects = document.Root.Elements().Where(e => e.Name.LocalName.StartsWith("SW.Blocks.", StringComparison.Ordinal)
                    || e.Name.LocalName == "SW.Types.PlcStruct").ToList();
                if (objects.Count != 1) throw new InvalidDataException("Expected one root PLC object.");
                var obj = objects[0];
                var kind = Kind(obj.Name.LocalName);
                if (kind != target.Kind) throw new InvalidDataException("XML kind differs from object inventory.");
                var attrs = obj.Elements().Single(e => e.Name.LocalName == "AttributeList");
                var names = attrs.Elements().Where(e => e.Name.LocalName == "Name").ToList();
                if (names.Count != 1 || names[0].Value != target.Name) throw new InvalidDataException("XML name differs from inventory.");
                var interfaces = attrs.Elements().Where(e => e.Name.LocalName == "Interface").ToList();
                if (interfaces.Count != 1) throw new InvalidDataException("Interface missing or ambiguous.");
                target.InterfaceXml = interfaces[0].ToString(SaveOptions.DisableFormatting);
                target.InterfaceComplete = true;
                var instance = attrs.Elements().Where(e => e.Name.LocalName == "InstanceOfName").ToList();
                if (obj.Name.LocalName == "SW.Blocks.InstanceDB")
                {
                    if (instance.Count != 1 || string.IsNullOrWhiteSpace(instance[0].Value))
                        throw new InvalidDataException("Instance relationship unavailable.");
                    target.InstanceOfName = instance[0].Value;
                }
                else
                {
                    if (instance.Count > 0) throw new InvalidDataException("Unexpected instance metadata on non-instance.");
                    target.InstanceOfName = "";
                }
                target.InstanceComplete = true;
            }
        }

        public static string Kind(string xmlObjectName)
        {
            switch (xmlObjectName)
            {
                case "SW.Blocks.OB": return "OB";
                case "SW.Blocks.FB": return "FB";
                case "SW.Blocks.FC": return "FC";
                case "SW.Blocks.GlobalDB":
                case "SW.Blocks.InstanceDB": return "DB";
                case "SW.Types.PlcStruct": return "UDT";
                default: throw new InvalidDataException("Unknown PLC XML object kind: " + xmlObjectName);
            }
        }
    }
}
