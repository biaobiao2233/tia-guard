using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace TiaGuard.Openness
{
    // A comparison projection, not a compiler. TIA remains responsible for SCL syntax.
    // Names, literals and every non-comment token are preserved; only formatting is ignored.
    public static class ProjectGenerationSclProjection
    {
        public const string Version = "scl-source-v1";
        public sealed class SourceObject
        {
            public string Kind { get; internal set; }
            public string Name { get; internal set; }
            public string Text { get; internal set; }
            public string InstanceOfName { get; internal set; }
            public string Interface { get; internal set; }
        }
        private sealed class Token
        {
            internal string Value;
            internal string Raw;
            internal int Start, End;
            internal bool Quoted;
        }
        private static readonly Dictionary<string, string> Kinds = new Dictionary<string, string>
        { ["ORGANIZATION_BLOCK"] = "OB", ["FUNCTION_BLOCK"] = "FB", ["FUNCTION"] = "FC", ["DATA_BLOCK"] = "DB", ["TYPE"] = "UDT" };
        private static readonly Dictionary<string, string> Sections = new Dictionary<string, string>
        { ["VAR_INPUT"] = "Input", ["VAR_OUTPUT"] = "Output", ["VAR_IN_OUT"] = "InOut",
            ["VAR"] = "Static", ["VAR_TEMP"] = "Temp", ["VAR_CONSTANT"] = "Constant" };

        public static IReadOnlyList<SourceObject> Parse(string text)
        {
            var tokens = Lex(text); var objects = new List<SourceObject>();
            for (int i = 0; i < tokens.Count;)
            {
                if (tokens[i].Value == ";") { i++; continue; }
                var first = tokens[i];
                if (first.Quoted || !Kinds.TryGetValue(first.Value, out var kind)) throw new InvalidDataException("Unexpected text outside SCL object.");
                var endName = "END_" + first.Value;
                if (++i >= tokens.Count) throw new InvalidDataException("Missing SCL object name.");
                var name = Identifier(tokens[i++]); var bodyStart = i;
                while (i < tokens.Count && (tokens[i].Quoted || tokens[i].Value != endName)) i++;
                if (i == tokens.Count) throw new InvalidDataException("Missing " + endName);
                var body = tokens.GetRange(bodyStart, i - bodyStart);
                var instance = kind == "DB" ? Instance(body) : null;
                objects.Add(new SourceObject { Kind = kind, Name = name,
                    Text = text.Substring(first.Start, tokens[i].End - first.Start),
                    InstanceOfName = instance, Interface = Interface(body, kind, name) });
                i++;
            }
            if (objects.Count == 0) throw new InvalidDataException("No SCL objects.");
            return objects;
        }

        public static string Normalize(string text)
        {
            var tokens = Lex(text);
            var begin = tokens.FindIndex(t => !t.Quoted && t.Value == "BEGIN");
            if (begin >= 0)
            {
                var locals = new HashSet<string>(StringComparer.Ordinal);
                bool inDeclarations = false;
                for (int i = 2; i < begin; i++)
                {
                    if (!tokens[i].Quoted && Sections.ContainsKey(tokens[i].Value)) inDeclarations = true;
                    else if (!tokens[i].Quoted && tokens[i].Value == "END_VAR") inDeclarations = false;
                    else if (inDeclarations && !tokens[i].Quoted && i + 1 < begin && tokens[i + 1].Value == ":") locals.Add(tokens[i].Value);
                }
                if (tokens.Count >= 2 && tokens[0].Value == "FUNCTION") locals.Add(Identifier(tokens[1]).ToUpperInvariant());
                if (tokens.Count >= 2 && tokens[0].Value == "ORGANIZATION_BLOCK" && Identifier(tokens[1]) == "Main")
                { locals.Add("INITIAL_CALL"); locals.Add("REMANENCE"); }
                // V21 qualifies unquoted references to declared locals with '#'. Only
                // declaration-bound locals in the body qualify; global quoted names,
                // strings, field selectors and already-qualified locals remain distinct.
                for (int i = tokens.Count - 1; i > begin; i--)
                    if (!tokens[i].Quoted && locals.Contains(tokens[i].Value) && tokens[i - 1].Value != "#" && tokens[i - 1].Value != ".")
                        tokens.Insert(i, new Token { Value = "#" });
            }
            // Observed on the owned V21/1212C cyclic OB1: TIA adds this default
            // title if omitted. Insert into the expected representation too; never
            // discard a user title or an arbitrary exported title.
            if (tokens.Count >= 2 && tokens[0].Value == "ORGANIZATION_BLOCK" && Identifier(tokens[1]) == "Main" &&
                !tokens.TakeWhile(t => t.Quoted || t.Value != "BEGIN").Any(t => !t.Quoted && t.Value == "TITLE"))
                tokens.InsertRange(2, new[] { new Token { Value = "TITLE" }, new Token { Value = "=" },
                    new Token { Value = "\"Main Program Sweep (Cycle)\"", Quoted = true } });
            // Empty VAR sections are the only declaration default removed. Nonempty
            // sections, attributes, version, declarations, instructions and literals remain.
            for (int i = tokens.Count - 2; i >= 0; i--)
                if (!tokens[i].Quoted && Sections.ContainsKey(tokens[i].Value) && tokens[i + 1].Value == "END_VAR")
                    tokens.RemoveRange(i, 2);
            return Pack(tokens.Select(t => t.Value));
        }

        public static string CyclicObInterface(string declared) => Pack(new[] {
            "Input/INITIAL_CALL:" + Pack(new[] { "BOOL" }),
            "Input/REMANENCE:" + Pack(new[] { "BOOL" }) }) + declared;

        public static string XmlInterface(string xml, string objectKind = null)
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using (var reader = XmlReader.Create(new StringReader(xml), settings))
            {
                var root = XElement.Load(reader);
                if (root.Name.LocalName != "Interface") throw new InvalidDataException("Missing observed Interface.");
                var rows = new List<string>();
                foreach (var section in root.Descendants().Where(e => e.Name.LocalName == "Section" &&
                    !e.Ancestors().Any(a => a.Name.LocalName == "Member")))
                {
                    var sectionName = (string)section.Attribute("Name");
                    if (sectionName == "None" && objectKind == "UDT") sectionName = "Static";
                    if (!Sections.Values.Contains(sectionName) && sectionName != "Return") throw new InvalidDataException("Unsupported observed interface section: " + sectionName);
                    foreach (var member in section.Elements().Where(e => e.Name.LocalName == "Member"))
                        XmlMember(member, sectionName, rows);
                }
                return Pack(rows);
            }
        }
        private static void XmlMember(XElement member, string path, List<string> rows)
        {
            var name = (string)member.Attribute("Name"); var type = (string)member.Attribute("Datatype");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(type)) throw new InvalidDataException("Unreadable interface member.");
            path += "/" + name.ToUpperInvariant();
            rows.Add(path + ":" + Pack(Lex(type).Select(t => t.Value)));
            if (string.Equals(type, "Struct", StringComparison.OrdinalIgnoreCase))
                foreach (var nested in member.Descendants().Where(e => e.Name.LocalName == "Member" &&
                    e.Ancestors().First(a => a.Name.LocalName == "Member") == member)) XmlMember(nested, path, rows);
        }
        private static string Interface(List<Token> tokens, string kind, string name)
        {
            var rows = new List<string>();
            for (int i = 0; i < tokens.Count; i++)
            {
                if (tokens[i].Quoted) continue;
                if (tokens[i].Value == "BEGIN") break;
                if (kind == "UDT" && tokens[i].Value == "STRUCT") { i++; Members(tokens, ref i, "Static", "END_STRUCT", rows); }
                else if (Sections.TryGetValue(tokens[i].Value, out var section))
                {
                    i++;
                    // RETAIN/NON_RETAIN affects storage, retained in full content comparison.
                    if (i < tokens.Count && (tokens[i].Value == "RETAIN" || tokens[i].Value == "NON_RETAIN")) i++;
                    Members(tokens, ref i, section, "END_VAR", rows);
                }
            }
            if (kind == "FC")
            {
                if (tokens.Count < 2 || tokens[0].Value != ":") throw new InvalidDataException("FC return declaration missing.");
                int i = 1; var type = new List<Token>();
                while (i < tokens.Count && (tokens[i].Quoted || tokens[i].Value != "VERSION") && tokens[i].Value != "{") type.Add(tokens[i++]);
                if (type.Count == 0) throw new InvalidDataException("FC return type missing.");
                if (Pack(type.Select(t => t.Value)) != Pack(new[] { "VOID" })) rows.Insert(0, "Return/" + name.ToUpperInvariant() + ":" + Pack(type.Select(t => t.Value)));
            }
            return Pack(rows);
        }
        private static void Members(List<Token> tokens, ref int i, string path, string end, List<string> rows)
        {
            while (i < tokens.Count && tokens[i].Value != end)
            {
                var name = Identifier(tokens[i++]).ToUpperInvariant();
                if (i >= tokens.Count || tokens[i++].Value != ":") throw new InvalidDataException("Only one name per SCL declaration is supported.");
                var type = new List<Token>();
                while (i < tokens.Count && tokens[i].Value != ";" && tokens[i].Value != ":=" && tokens[i].Value != "STRUCT") type.Add(tokens[i++]);
                if (i < tokens.Count && tokens[i].Value == "STRUCT")
                {
                    if (type.Count != 0) throw new InvalidDataException("Unsupported inline STRUCT declaration.");
                    rows.Add(path + "/" + name + ":" + Pack(new[] { "STRUCT" })); i++;
                    Members(tokens, ref i, path + "/" + name, "END_STRUCT", rows); i++;
                }
                else
                {
                    if (type.Count == 0) throw new InvalidDataException("Missing interface data type.");
                    rows.Add(path + "/" + name + ":" + Pack(type.Select(t => t.Value)));
                }
                // Initial values remain significant in full source comparison.
                while (i < tokens.Count && tokens[i].Value != ";") i++;
                if (i == tokens.Count) throw new InvalidDataException("Unterminated SCL declaration."); i++;
            }
            if (i == tokens.Count) throw new InvalidDataException("Missing interface terminator " + end);
        }
        private static string Instance(List<Token> tokens)
        {
            var begin = tokens.FindIndex(t => !t.Quoted && t.Value == "BEGIN");
            if (begin < 0) throw new InvalidDataException("DB BEGIN is missing.");
            if (tokens.Take(begin).Any(t => !t.Quoted && t.Value.StartsWith("VAR", StringComparison.Ordinal))) return null;
            if (begin == 0 || !tokens[begin - 1].Quoted || !tokens[begin - 1].Value.StartsWith("\"", StringComparison.Ordinal))
                throw new InvalidDataException("Instance DB requires a named FB immediately before BEGIN.");
            return Identifier(tokens[begin - 1]);
        }
        private static string Identifier(Token t)
        {
            var name = t.Value.StartsWith("\"", StringComparison.Ordinal) ? t.Value.Substring(1, t.Value.Length - 2) : t.Raw;
            if (!ProjectGenerationSpecLoader.Identifier(name)) throw new InvalidDataException("Unsupported SCL identifier: " + name);
            return name;
        }
        private static string Pack(IEnumerable<string> values) => string.Concat(values.Select(v => v.Length + ":" + v));
        private static List<Token> Lex(string text)
        {
            if (text == null) throw new InvalidDataException("Missing SCL text.");
            var result = new List<Token>();
            for (int i = 0; i < text.Length;)
            {
                if (char.IsWhiteSpace(text[i]) || text[i] == '\ufeff') { i++; continue; }
                if (i + 1 < text.Length && text.Substring(i, 2) == "//") { while (i < text.Length && text[i] != '\n') i++; continue; }
                if (i + 1 < text.Length && (text.Substring(i, 2) == "(*" || text.Substring(i, 2) == "/*"))
                {
                    var open = text.Substring(i, 2); var close = open == "(*" ? "*)" : "*/"; int depth = 1; i += 2;
                    while (depth > 0 && i + 1 < text.Length)
                    { if (text.Substring(i, 2) == open) { depth++; i += 2; } else if (text.Substring(i, 2) == close) { depth--; i += 2; } else i++; }
                    if (depth != 0) throw new InvalidDataException("Unterminated SCL comment."); continue;
                }
                var start = i; var c = text[i++]; bool quoted = c == '\'' || c == '"';
                if (quoted)
                {
                    bool closed = false;
                    while (i < text.Length)
                    {
                        var next = text[i++];
                        if (next == '$') { if (i == text.Length) break; i++; continue; }
                        if (next == c) { if (i < text.Length && text[i] == c) { i++; continue; } closed = true; break; }
                    }
                    if (!closed) throw new InvalidDataException("Unterminated SCL literal.");
                }
                else if (char.IsLetterOrDigit(c) || c == '_')
                    while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                else if (i < text.Length && new[] { ":=", "=>", "<=", ">=", "<>", "..", "**" }.Contains(text.Substring(start, 2))) i++;
                var value = text.Substring(start, i - start);
                result.Add(new Token { Value = quoted ? value : value.ToUpperInvariant(), Raw = value, Quoted = quoted, Start = start, End = i });
            }
            return result;
        }
    }
}
