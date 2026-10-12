using System.Text;

namespace TiaGuard.TwinCat.Validation;

public static class StTokens
{
    // Only whitespace, comments and identifier case are normalized. Literals/pragmas/operators remain significant.
    public static string Canonicalize(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = new StringBuilder();
        var i = 0;
        void Append(string token) => result.Append(token.Length).Append(':').Append(token).Append(';');
        while (i < source.Length)
        {
            if (char.IsWhiteSpace(source[i]) || source[i] == '\uFEFF') { i++; continue; }
            if (i + 1 < source.Length && source[i] == '/' && source[i + 1] == '/')
            {
                i += 2;
                while (i < source.Length && source[i] != '\r' && source[i] != '\n') i++;
                continue;
            }
            if (i + 1 < source.Length && source[i] == '(' && source[i + 1] == '*')
            {
                i += 2;
                var depth = 1;
                while (i < source.Length && depth != 0)
                {
                    if (i + 1 < source.Length && source[i] == '(' && source[i + 1] == '*') { depth++; i += 2; }
                    else if (i + 1 < source.Length && source[i] == '*' && source[i + 1] == ')') { depth--; i += 2; }
                    else i++;
                }
                if (depth != 0) throw new FormatException("Unterminated ST comment.");
                continue;
            }
            var start = i;
            if (source[i] is '\'' or '"')
            {
                var quote = source[i++];
                var closed = false;
                while (i < source.Length)
                {
                    if (source[i] == '$') { i += 2; continue; }
                    if (source[i++] != quote) continue;
                    if (i < source.Length && source[i] == quote) { i++; continue; }
                    closed = true;
                    break;
                }
                if (!closed || i > source.Length) throw new FormatException("Unterminated ST literal.");
                Append(source[start..i]);
            }
            else if (char.IsLetter(source[i]) || source[i] == '_')
            {
                while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_')) i++;
                Append(source[start..i].ToUpperInvariant());
            }
            else if (char.IsDigit(source[i]))
            {
                // Preserve numeric spelling; no unproven numeric/literal equivalence.
                while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_' || source[i] == '#')) i++;
                Append(source[start..i]);
            }
            else
            {
                i++;
                if (i < source.Length && new[] { ":=", "=>", "<=", ">=", "<>", "**", ".." }.Contains(source[start..(i + 1)], StringComparer.Ordinal)) i++;
                Append(source[start..i]);
            }
        }
        return result.ToString();
    }
}
