using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace VladislavTsurikov.AnalyzeDependencies.Editor.Core.Utilities
{
    public static class GitRepositoryUtility
    {
        public static bool HasGitRepository(string repositoryRoot)
        {
            string gitPath = GetGitPath(repositoryRoot);
            return Directory.Exists(gitPath) || File.Exists(gitPath);
        }

        public static string InitializeRepository(string repositoryRoot)
        {
            return ExecuteGitCommand(repositoryRoot, "init");
        }

        public static void RemoveGitRepositoryMarker(string repositoryRoot)
        {
            string gitPath = GetGitPath(repositoryRoot);
            if (Directory.Exists(gitPath))
            {
                Directory.Delete(gitPath, true);
                return;
            }

            if (File.Exists(gitPath))
            {
                File.Delete(gitPath);
            }
        }

        public static string GetRepositoryRoot(string workingDirectory)
        {
            return ExecuteGitCommand(workingDirectory, "rev-parse --show-toplevel").Trim();
        }

        public static bool IsRegisteredSubmodule(string rootRepositoryPath, string repositoryRoot)
        {
            if (!HasGitRepository(rootRepositoryPath) || !HasGitRepository(repositoryRoot))
                return false;

            string fullRootPath = Path.GetFullPath(rootRepositoryPath);
            string fullRepositoryPath = Path.GetFullPath(repositoryRoot);

            if (!fullRepositoryPath.StartsWith(fullRootPath, StringComparison.OrdinalIgnoreCase))
                return false;

            string relativePath = Path.GetRelativePath(fullRootPath, fullRepositoryPath).Replace('\\', '/');
            Dictionary<string, GitModuleEntry> entries = ReadGitModules(Path.Combine(rootRepositoryPath, ".gitmodules"));

            if (!entries.TryGetValue(relativePath, out GitModuleEntry entry) || entry.Path != relativePath)
                return false;

            string lsFilesOutput = ExecuteGitCommand(rootRepositoryPath, $"ls-files --stage -- \"{relativePath}\"", allowEmptyOutput: true);
            if (string.IsNullOrWhiteSpace(lsFilesOutput))
                return false;

            foreach (string line in lsFilesOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("160000 ", StringComparison.Ordinal) &&
                    line.EndsWith("\t" + relativePath, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public static string RegisterSubmodule(string rootRepositoryPath, string repositoryRoot, string initialCommitMessage = null)
        {
            if (!HasGitRepository(rootRepositoryPath))
                throw new InvalidOperationException("Root git repository was not found.");

            if (!HasGitRepository(repositoryRoot))
                throw new InvalidOperationException("Selected asmdef folder does not contain a git repository.");

            string fullRootPath = Path.GetFullPath(rootRepositoryPath);
            string fullRepositoryPath = Path.GetFullPath(repositoryRoot);

            if (!fullRepositoryPath.StartsWith(fullRootPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Selected asmdef folder must be inside the root repository.");

            string relativePath = Path.GetRelativePath(fullRootPath, fullRepositoryPath).Replace('\\', '/');
            if (IsRegisteredSubmodule(rootRepositoryPath, repositoryRoot))
                return $"Skipped {relativePath}: already registered as a submodule.";

            initialCommitMessage ??= EnsureRepositoryHasCommit(repositoryRoot);
            string headCommit = ExecuteGitCommand(repositoryRoot, "rev-parse HEAD").Trim();
            string remoteUrl = TryGetRemoteOriginUrl(repositoryRoot);
            bool usesLocalPath = string.IsNullOrWhiteSpace(remoteUrl);

            RemovePathFromIndex(rootRepositoryPath, relativePath);
            WriteGitModule(rootRepositoryPath, new GitModuleEntry
            {
                Name = relativePath,
                Path = relativePath,
                Url = usesLocalPath ? fullRepositoryPath.Replace('\\', '/') : remoteUrl
            });

            ExecuteGitCommand(rootRepositoryPath, "add .gitmodules", allowEmptyOutput: true);
            UpdateGitIndexWithSubmodule(rootRepositoryPath, relativePath, headCommit);
            TryAbsorbGitDirectory(rootRepositoryPath, relativePath);

            var summary = new StringBuilder();
            summary.Append($"Registered {relativePath}");
            summary.Append(usesLocalPath
                ? " as a submodule using the local repository path."
                : " as a submodule using remote.origin.url.");

            if (!string.IsNullOrWhiteSpace(initialCommitMessage))
            {
                summary.Append(' ');
                summary.Append(initialCommitMessage);
            }

            return summary.ToString();
        }

        public static bool RepositoryHasCommit(string repositoryRoot)
        {
            try
            {
                ExecuteGitCommand(repositoryRoot, "rev-parse --verify HEAD", allowEmptyOutput: true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string CreateInitialCommit(string repositoryRoot)
        {
            ExecuteGitCommand(repositoryRoot, "add -A", allowEmptyOutput: true);
            ExecuteGitCommand(repositoryRoot, "commit -m \"Initial package commit\" --allow-empty", allowEmptyOutput: true);
            string shortCommit = ExecuteGitCommand(repositoryRoot, "rev-parse --short HEAD").Trim();
            return $"Created initial commit {shortCommit}.";
        }

        public static string EnsureRepositoryHasCommit(string repositoryRoot)
        {
            if (RepositoryHasCommit(repositoryRoot))
                return string.Empty;

            return CreateInitialCommit(repositoryRoot);
        }

        private static string GetGitPath(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, ".git");
        }

        private static string TryGetRemoteOriginUrl(string repositoryRoot)
        {
            try
            {
                return ExecuteGitCommand(repositoryRoot, "config --get remote.origin.url", allowEmptyOutput: true).Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void RemovePathFromIndex(string rootRepositoryPath, string relativePath)
        {
            ExecuteGitCommand(rootRepositoryPath, $"rm -r --cached --ignore-unmatch -- \"{relativePath}\"", allowEmptyOutput: true);
        }

        private static void UpdateGitIndexWithSubmodule(string rootRepositoryPath, string relativePath, string headCommit)
        {
            string input = $"160000 {headCommit}\t{relativePath}{Environment.NewLine}";
            ExecuteGitCommand(rootRepositoryPath, "update-index --index-info", allowEmptyOutput: true, standardInput: input);
        }

        private static void WriteGitModule(string rootRepositoryPath, GitModuleEntry entry)
        {
            string gitModulesPath = Path.Combine(rootRepositoryPath, ".gitmodules");
            Dictionary<string, GitModuleEntry> entries = ReadGitModules(gitModulesPath);
            entries[entry.Name] = entry;

            var output = new StringBuilder();
            foreach (GitModuleEntry gitModuleEntry in entries.Values.OrderBy(value => value.Path, StringComparer.Ordinal))
            {
                output.Append("[submodule \"");
                output.Append(gitModuleEntry.Name);
                output.AppendLine("\"]");
                output.Append("\tpath = ");
                output.AppendLine(gitModuleEntry.Path);
                output.Append("\turl = ");
                output.AppendLine(gitModuleEntry.Url);
                output.AppendLine();
            }

            File.WriteAllText(gitModulesPath, output.ToString().TrimEnd() + Environment.NewLine);
        }

        private static Dictionary<string, GitModuleEntry> ReadGitModules(string gitModulesPath)
        {
            var entries = new Dictionary<string, GitModuleEntry>(StringComparer.Ordinal);
            if (!File.Exists(gitModulesPath))
                return entries;

            GitModuleEntry currentEntry = null;
            foreach (string line in File.ReadAllLines(gitModulesPath))
            {
                string trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                    continue;

                if (trimmed.StartsWith("[submodule \"", StringComparison.Ordinal) &&
                    trimmed.EndsWith("\"]", StringComparison.Ordinal))
                {
                    string name = trimmed.Substring(12, trimmed.Length - 14);
                    currentEntry = new GitModuleEntry { Name = name };
                    entries[name] = currentEntry;
                    continue;
                }

                if (currentEntry == null)
                    continue;

                int separatorIndex = trimmed.IndexOf('=');
                if (separatorIndex < 0)
                    continue;

                string key = trimmed.Substring(0, separatorIndex).Trim();
                string value = trimmed.Substring(separatorIndex + 1).Trim();

                if (key == "path")
                    currentEntry.Path = value;
                else if (key == "url")
                    currentEntry.Url = value;
            }

            return entries;
        }

        private static void TryAbsorbGitDirectory(string rootRepositoryPath, string relativePath)
        {
            try
            {
                ExecuteGitCommand(rootRepositoryPath, $"submodule absorbgitdirs -- \"{relativePath}\"", allowEmptyOutput: true);
            }
            catch
            {
            }
        }

        private static string ExecuteGitCommand(string workingDirectory, string arguments, bool allowEmptyOutput = false)
        {
            return ExecuteGitCommand(workingDirectory, arguments, allowEmptyOutput, null);
        }

        private static string ExecuteGitCommand(string workingDirectory, string arguments, bool allowEmptyOutput, string standardInput)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardInput = standardInput != null,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (var process = new Process { StartInfo = startInfo })
            {
                process.Start();

                if (standardInput != null)
                {
                    process.StandardInput.Write(standardInput);
                    process.StandardInput.Close();
                }

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                        ? $"git {arguments} failed."
                        : error.Trim());
                }

                if (string.IsNullOrWhiteSpace(output) && allowEmptyOutput)
                    return string.Empty;

                return string.IsNullOrWhiteSpace(output) ? $"git {arguments} completed." : output.Trim();
            }
        }

        private sealed class GitModuleEntry
        {
            public string Name;
            public string Path;
            public string Url;
        }
    }
}
