using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TiaGuard.Openness
{
    internal static class ProjectGenerationSpecSource
    {
        private static readonly Dictionary<string, string> Kinds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        { ["ORGANIZATION_BLOCK"] = "OB", ["FUNCTION_BLOCK"] = "FB", ["FUNCTION"] = "FC", ["DATA_BLOCK"] = "DB", ["TYPE"] = "UDT" };

        // Lexical envelope only: no expression, interface, block-number or dependency inference.
        // Comments and quoted literals cannot invent headers. Unknown syntax is left to V21.
        internal static void Validate(byte[] bytes, IList<ProjectGenerationArtifact> declarations)
        {
            var text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\ufeff');
            var tokens = Tokens(text).ToList();
            var found = new List<string>(); string end = null;
            for (var i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                if (token.Quoted) continue;
                if (Kinds.TryGetValue(token.Text, out var kind))
                {
                    if (end != null || i + 1 == tokens.Count) throw new InvalidDataException("Nested/incomplete SCL object envelope.");
                    var name = tokens[++i];
                    if (name.StringLiteral || !ProjectGenerationSpecLoader.Identifier(name.Text)) throw new InvalidDataException("Invalid SCL object name.");
                    found.Add(kind + ":" + name.Text);
                    end = "END_" + token.Text.ToUpperInvariant();
                }
                else if (token.Text.StartsWith("END_", StringComparison.OrdinalIgnoreCase) &&
                    Kinds.ContainsKey(token.Text.Substring(4)))
                {
                    if (!string.Equals(token.Text, end, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Mismatched SCL object terminator.");
                    end = null;
                }
                else if (end == null && token.Text != ";") throw new InvalidDataException("Unsupported text outside SCL object envelope.");
            }
            if (end != null || found.Count != declarations.Count || found.Distinct(StringComparer.OrdinalIgnoreCase).Count() != found.Count ||
                !found.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(declarations.Select(x => x.Kind + ":" + x.Name).OrderBy(x => x, StringComparer.Ordinal)))
                throw new InvalidDataException("SCL declared object identities do not match source envelopes.");
        }
        private sealed class Token { internal string Text; internal bool Quoted; internal bool StringLiteral; }
        private static IEnumerable<Token> Tokens(string text)
        {
            for (var i = 0; i < text.Length;)
            {
                var c = text[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (i + 1 < text.Length && text.Substring(i, 2) == "//")
                { while (i < text.Length && text[i] != '\n') i++; continue; }
                if (i + 1 < text.Length && (text.Substring(i, 2) == "(*" || text.Substring(i, 2) == "/*"))
                {
                    var close = text.Substring(i, 2) == "(*" ? "*)" : "*/";
                    var finish = text.IndexOf(close, i + 2, StringComparison.Ordinal);
                    if (finish < 0) throw new InvalidDataException("Unterminated SCL comment.");
                    i = finish + 2; continue;
                }
                if (c == '"' || c == '\'')
                {
                    var quote = c; var value = new StringBuilder(); i++; var closed = false;
                    while (i < text.Length)
                    {
                        c = text[i++];
                        if (c == '$' && i < text.Length) { value.Append(c).Append(text[i++]); continue; }
                        if (c == quote)
                        {
                            if (i < text.Length && text[i] == quote) { value.Append(c); i++; continue; }
                            closed = true; break;
                        }
                        value.Append(c);
                    }
                    if (!closed) throw new InvalidDataException("Unterminated SCL literal.");
                    yield return new Token { Text = value.ToString(), Quoted = true, StringLiteral = quote == '\'' }; continue;
                }
                var start = i++;
                if (char.IsLetter(c) || c == '_') while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                yield return new Token { Text = text.Substring(start, i - start) };
            }
        }
    }
}
