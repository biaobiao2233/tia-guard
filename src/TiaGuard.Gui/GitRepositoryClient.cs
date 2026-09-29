using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace TiaGuard.Gui
{
    internal sealed class GitOperationException : InvalidOperationException
    {
        internal string FriendlyMessage { get; private set; }

        internal GitOperationException(string friendlyMessage, string technicalMessage = null,
            Exception inner = null)
            : base(technicalMessage ?? friendlyMessage, inner)
        {
            FriendlyMessage = friendlyMessage;
        }
    }

    internal sealed class GitRepositoryClient
    {
        private sealed class GitResult
        {
            internal int ExitCode;
            internal string Output;
            internal string Error;
        }

        internal string PrepareRepository(string repositoryUrl, Action<string> log)
        {
            var url = ValidateRepositoryUrl(repositoryUrl);
            EnsureGitAvailable();
            var cacheRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TIA-Guard", "git");
            Directory.CreateDirectory(cacheRoot);
            var root = Path.Combine(cacheRoot, Hash(url).Substring(0, 20));

            if (!Directory.Exists(root))
            {
                log?.Invoke("git: clone -> managed cache");
                var clone = Run(null, new[] { "clone", "--", url, root }, false);
                EnsureSuccess("clone", clone);
            }
            else
            {
                if (!Directory.Exists(Path.Combine(root, ".git")))
                    throw new GitOperationException(
                        "本地 Git 缓存不是有效仓库。请删除该缓存目录后重试。",
                        "Managed Git cache is not a repository: " + root);

                var remote = Run(root, new[] { "remote", "get-url", "origin" }, false);
                EnsureSuccess("remote", remote);
                if (!SameRepositoryUrl(url, remote.Output.Trim()))
                    throw new GitOperationException(
                        "本地 Git 缓存的 origin 与当前 URL 不一致，已停止以避免操作错误仓库。",
                        "Origin URL mismatch in managed cache.");

                EnsureClean(root);
                log?.Invoke("git: fetch origin");
                EnsureSuccess("fetch", Run(root, new[] { "fetch", "--prune", "origin" }, false));

                if (HasHead(root) && HasUpstream(root))
                {
                    log?.Invoke("git: pull --ff-only");
                    EnsureSuccess("pull", Run(root, new[] { "pull", "--ff-only" }, false));
                }
            }

            EnsureClean(root);
            log?.Invoke("git workspace: " + root);
            return root;
        }

        internal void EnsureCommitIdentity(string repositoryRoot)
        {
            var name = Run(repositoryRoot, new[] { "config", "--get", "user.name" }, true);
            var email = Run(repositoryRoot, new[] { "config", "--get", "user.email" }, true);
            if (name.ExitCode != 0 || string.IsNullOrWhiteSpace(name.Output) ||
                email.ExitCode != 0 || string.IsNullOrWhiteSpace(email.Output))
                throw new GitOperationException(
                    "系统 Git 还没有可用的 user.name / user.email。请先在 Git 中配置提交身份，TIA-Guard 不会保存账号或 Token。",
                    "Git commit identity is not configured.");
        }

        internal bool CommitManagedSource(
            string repositoryRoot, string message, Action<string> log)
        {
            EnsureCommitIdentity(repositoryRoot);
            log?.Invoke("git: add tia-source/");
            EnsureSuccess("add", Run(repositoryRoot,
                new[] { "add", "--", "tia-source" }, false));

            var diff = Run(repositoryRoot,
                new[] { "diff", "--cached", "--quiet", "--", "tia-source" }, true);
            if (diff.ExitCode == 0)
            {
                log?.Invoke("git: tia-source/ 无变化，无需新 commit");
                return false;
            }
            if (diff.ExitCode != 1) EnsureSuccess("diff", diff);

            var commitMessage = string.IsNullOrWhiteSpace(message)
                ? "Update TIA source " + DateTime.Now.ToString("yyyy-MM-dd HH:mm")
                : message.Trim();
            log?.Invoke("git: commit");
            EnsureSuccess("commit", Run(repositoryRoot,
                new[] { "commit", "-m", commitMessage }, false));
            return true;
        }

        internal void Push(string repositoryRoot, Action<string> log)
        {
            if (!HasHead(repositoryRoot))
                throw new GitOperationException("仓库还没有可推送的提交。", "Git repository has no HEAD.");

            log?.Invoke("git: push");
            GitResult result;
            if (HasUpstream(repositoryRoot))
                result = Run(repositoryRoot, new[] { "push" }, false);
            else
                result = Run(repositoryRoot, new[] { "push", "-u", "origin", "HEAD" }, false);
            EnsureSuccess("push", result);
        }

        private static void EnsureClean(string root)
        {
            var status = Run(root, new[] { "status", "--porcelain" }, false);
            EnsureSuccess("status", status);
            if (!string.IsNullOrWhiteSpace(status.Output))
                throw new GitOperationException(
                    "TIA-Guard 的本地 Git 缓存存在未提交改动，已停止以避免覆盖。请先检查该缓存仓库。",
                    "Managed Git cache has uncommitted changes:\n" + status.Output);
        }

        private static bool HasHead(string root)
        {
            return Run(root, new[] { "rev-parse", "--verify", "HEAD" }, true).ExitCode == 0;
        }

        private static bool HasUpstream(string root)
        {
            return Run(root,
                new[] { "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{u}" },
                true).ExitCode == 0;
        }

        private static string ValidateRepositoryUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                throw new GitOperationException("请输入有效的 Git 仓库 URL。");
            var url = value.Trim();
            Uri parsed;
            if (Uri.TryCreate(url, UriKind.Absolute, out parsed) &&
                (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps) &&
                !string.IsNullOrEmpty(parsed.UserInfo))
                throw new GitOperationException(
                    "仓库 URL 中不能内嵌账号、密码或 Token。请使用系统 Git Credential Manager / SSH 凭据。",
                    "Repository URL contains embedded credentials.");
            return url;
        }

        private static bool SameRepositoryUrl(string a, string b)
        {
            return string.Equals(NormalizeUrl(a), NormalizeUrl(b),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeUrl(string value)
        {
            var normalized = (value ?? string.Empty).Trim().TrimEnd('/');
            if (normalized.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring(0, normalized.Length - 4);
            return normalized;
        }

        private static string Hash(string value)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value)))
                    .Replace("-", string.Empty).ToLowerInvariant();
        }

        private static void EnsureGitAvailable()
        {
            try
            {
                EnsureSuccess("version", Run(null, new[] { "--version" }, false));
            }
            catch (GitOperationException) { throw; }
            catch (Win32Exception error)
            {
                throw new GitOperationException(
                    "未找到系统 Git。请先安装 Git for Windows，并确保 git.exe 在 PATH 中。",
                    error.Message, error);
            }
        }

        private static GitResult Run(string workingDirectory, string[] arguments, bool allowFailure)
        {
            try
            {
                var info = new ProcessStartInfo
                {
                    FileName = "git.exe",
                    Arguments = string.Join(" ", arguments.Select(QuoteArgument)),
                    WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                        ? Environment.CurrentDirectory : workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                info.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
                using (var process = Process.Start(info))
                {
                    var outputTask = process.StandardOutput.ReadToEndAsync();
                    var errorTask = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(120000))
                    {
                        try { process.Kill(); } catch { }
                        throw new GitOperationException(
                            "Git 操作等待超过 2 分钟。请检查网络、SSH Agent 或系统 Git 凭据后重试。",
                            "Git process timed out.");
                    }
                    var result = new GitResult
                    {
                        ExitCode = process.ExitCode,
                        Output = outputTask.Result ?? string.Empty,
                        Error = errorTask.Result ?? string.Empty
                    };
                    if (!allowFailure && result.ExitCode != 0) EnsureSuccess("git", result);
                    return result;
                }
            }
            catch (GitOperationException) { throw; }
            catch (Win32Exception error)
            {
                throw new GitOperationException(
                    "无法启动系统 Git。请确认 Git for Windows 已安装并加入 PATH。",
                    error.Message, error);
            }
        }

        private static void EnsureSuccess(string operation, GitResult result)
        {
            if (result.ExitCode == 0) return;
            var technical = ((result.Error ?? string.Empty) + "\n" +
                (result.Output ?? string.Empty)).Trim();
            var lower = technical.ToLowerInvariant();
            string friendly;
            if (lower.Contains("authentication failed") ||
                lower.Contains("could not read username") ||
                lower.Contains("permission denied (publickey") ||
                lower.Contains("terminal prompts disabled"))
                friendly = "Git 认证失败。TIA-Guard 不保存 Token；请先用系统 Git Credential Manager、SSH Agent 或现有 Git 凭据完成登录。";
            else if (lower.Contains("repository not found"))
                friendly = "找不到仓库，或当前 Git 凭据没有访问权限。请确认 URL 与私有仓库权限。";
            else if (lower.Contains("non-fast-forward") || lower.Contains("fetch first") ||
                lower.Contains("rejected"))
                friendly = "远端分支已经发生变化，Git 拒绝非快进更新。TIA-Guard 已停止，不会强推或覆盖远端历史。";
            else if (lower.Contains("could not resolve host") ||
                lower.Contains("failed to connect") || lower.Contains("unable to access"))
                friendly = "Git 无法连接远端仓库。请检查网络、代理和仓库地址。";
            else if (lower.Contains("ssl certificate"))
                friendly = "Git 的 TLS/证书校验失败。请检查系统 Git 的证书与代理配置。";
            else if (operation == "commit")
                friendly = "Git 提交失败。请检查系统 Git 的 user.name / user.email 与仓库状态。";
            else
                friendly = "Git 操作失败（" + operation + "）。请查看技术日志中的 Git 错误。";
            throw new GitOperationException(friendly,
                "git " + operation + " failed with exit code " + result.ExitCode + ": " + technical);
        }

        private static string QuoteArgument(string value)
        {
            if (value == null) return "\"\"";
            if (value.Length != 0 && value.All(ch => !char.IsWhiteSpace(ch) && ch != '"'))
                return value;

            var builder = new StringBuilder("\"");
            var backslashes = 0;
            foreach (var ch in value)
            {
                if (ch == '\\')
                {
                    backslashes++;
                    continue;
                }
                if (ch == '"')
                {
                    builder.Append('\\', backslashes * 2 + 1);
                    builder.Append('"');
                    backslashes = 0;
                    continue;
                }
                builder.Append('\\', backslashes);
                backslashes = 0;
                builder.Append(ch);
            }
            builder.Append('\\', backslashes * 2);
            builder.Append('"');
            return builder.ToString();
        }
    }
}
