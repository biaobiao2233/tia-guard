using System.Text.RegularExpressions;

namespace TiaGuard.AI.Privacy;

internal sealed class SensitiveDataRedactor
{
    private static readonly Regex WindowsPath = new(
        @"(?<![A-Za-z0-9])(?:[A-Za-z]:\\|\\\\)[^\r\n\t\""<>|]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex UnixHomePath = new(
        @"(?<![A-Za-z0-9])/(?:home|Users|root|private|Volumes|mnt|media|workspaces?|tmp|var/tmp)/[^\s\""'<>]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex EmailAddress = new(
        @"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex AccountAssignment = new(
        @"\b(?:user(?:name)?|account|login)\s*[:=]\s*[^\s,;]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex CredentialAssignment = new(
        @"\b(?:api[_-]?key|access[_-]?token|refresh[_-]?token|client[_-]?secret|password|secret)\s*[:=]\s*[^\s,;]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex KnownToken = new(
        @"\b(?:sk-[A-Za-z0-9_-]{12,}|ghp_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|xox[baprs]-[A-Za-z0-9-]{12,})\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly string[] _additionalSensitiveValues;

    public SensitiveDataRedactor(PromptPrivacyOptions? options)
    {
        _additionalSensitiveValues = (options?.AdditionalSensitiveValues ?? Array.Empty<string>())
            .Where(static value => !string.IsNullOrEmpty(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(static value => value.Length)
            .ToArray();
    }

    public string Redact(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var redacted = WindowsPath.Replace(value, "[LOCAL_PATH]");
        redacted = UnixHomePath.Replace(redacted, "[LOCAL_PATH]");
        redacted = EmailAddress.Replace(redacted, "[EMAIL]");
        redacted = AccountAssignment.Replace(redacted, "[ACCOUNT]");
        redacted = CredentialAssignment.Replace(redacted, "[CREDENTIAL]");
        redacted = KnownToken.Replace(redacted, "[TOKEN]");

        foreach (var sensitiveValue in _additionalSensitiveValues)
        {
            redacted = redacted.Replace(sensitiveValue, "[REDACTED]", StringComparison.OrdinalIgnoreCase);
        }

        return redacted;
    }
}
