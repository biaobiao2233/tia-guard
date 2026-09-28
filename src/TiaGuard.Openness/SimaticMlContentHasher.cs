using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace TiaGuard.Openness
{
    public static class SimaticMlContentHasher
    {
        public const string NormalizationVersion = "simaticml-content-v1";

        // The raw export SHA remains byte evidence. This digest describes the XML
        // after removing only the proven volatile root Created timestamp.
        public static string ComputeSha256(string source)
        {
            var document = ReadDocument(File.ReadAllBytes(source));
            document.DocumentElement["DocumentInfo"]["Created"].InnerText = "1970-01-01T00:00:00Z";
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(document.OuterXml));
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        internal static XmlDocument ReadDocument(byte[] bytes)
        {
            var offset = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf
                ? 3 : 0;
            XmlDocument document;
            try
            {
                var text = new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset);
                document = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
                using (var reader = XmlReader.Create(new StringReader(text), settings))
                    document.Load(reader);
            }
            catch (DecoderFallbackException error)
            {
                throw new InvalidDataException("Invalid SimaticML UTF-8.", error);
            }
            catch (XmlException error)
            {
                throw new InvalidDataException("Invalid SimaticML XML.", error);
            }

            var root = document.DocumentElement;
            if (root == null || root.Name != "Document" || root.NamespaceURI.Length != 0)
                throw new InvalidDataException("Unexpected SimaticML document root.");
            XmlElement documentInfo = null;
            foreach (XmlNode child in root.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element || child.Name != "DocumentInfo") continue;
                if (documentInfo != null)
                    throw new InvalidDataException("Multiple root DocumentInfo elements.");
                documentInfo = (XmlElement)child;
            }
            if (documentInfo == null || documentInfo.HasAttributes)
                throw new InvalidDataException("Unexpected root DocumentInfo shape.");
            XmlElement created = null;
            foreach (XmlNode child in documentInfo.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element) continue;
                if (created == null && child.Name != "Created")
                    throw new InvalidDataException("Created must be the first DocumentInfo element.");
                if (child.Name != "Created") continue;
                if (created != null)
                    throw new InvalidDataException("Multiple root Created elements.");
                created = (XmlElement)child;
            }
            if (created == null || created.HasAttributes || created.ChildNodes.Count != 1 ||
                created.FirstChild.NodeType != XmlNodeType.Text)
                throw new InvalidDataException("Unexpected root Created shape.");

            return document;
        }
    }
}
