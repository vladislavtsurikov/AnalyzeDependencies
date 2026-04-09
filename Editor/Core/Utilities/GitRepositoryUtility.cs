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

        public static string RegisterSubmodule(string rootRepositoryPath, string repositoryRoot)
        {
            BatchRegistrationResult result = RegisterSubmodules(rootRepositoryPath, new[] { repositoryRoot });
            return result.Messages.Count > 0 ? result.Messages[0] : "Submodule registered.";
        }

        public static BatchRegistrationResult RegisterSubmodules(string rootRepositoryPath, IReadOnlyList<string> repositoryRoots)
        {
            if (!HasGitRepository(rootRepositoryPath))
                throw new InvalidOperationException("Root git repository was not found.");

            string fullRootPath = Path.GetFullPath(rootRepositoryPath);
            var registrations = new List<SubmoduleRegistration>();

            foreach (string repositoryRoot in repositoryRoots)
            {
                if (!HasGitRepository(repositoryRoot))
                    throw new InvalidOperationException("Selected asmdef folder does not contain a git repository.");

                string fullRepositoryPath = Path.GetFullPath(repositoryRoot);
                if (!fullRepositoryPath.StartsWith(fullRootPath, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Selected asmdef folder must be inside the root repository.");

                string relativePath = Path.GetRelativePath(fullRootPath, fullRepositoryPath).Replace('\\', '/');
                string initialCommitMessage = EnsureRepositoryHasCommit(repositoryRoot);
                string headCommit = ExecuteGitCommand(repositoryRoot, "rev-parse HEAD").Trim();
                string remoteUrl = TryGetRemoteOriginUrl(repositoryRoot);

                registrations.Add(new SubmoduleRegistration
                {
                    RelativePath = relativePath,
                    Name = relativePath,
                    Url = string.IsNullOrWhiteSpace(remoteUrl) ? fullRepositoryPath.Replace('\\', '/') : remoteUrl,
                    HeadCommit = headCommit,
                    InitialCommitMessage = initialCommitMessage,
                    UsesLocalPath = string.IsNullOrWhiteSpace(remoteUrl)
                });
            }

            if (registrations.Count == 0)
                return new BatchRegistrationResult();

            RemovePathsFromIndex(rootRepositoryPath, registrations.Select(registration => registration.RelativePath).ToList());
            WriteGitModules(rootRepositoryPath, registrations);
            ExecuteGitCommand(rootRepositoryPath, "add .gitmodules", allowEmptyOutput: true);
            UpdateGitIndexWithSubmodules(rootRepositoryPath, registrations);
            TryAbsorbGitDirectories(rootRepositoryPath, registrations.Select(registration => registration.RelativePath).ToList());

            var result = new BatchRegistrationResult();
            foreach (SubmoduleRegistration registration in registrations)
            {
                var summary = new StringBuilder();
                summary.Append($"Registered {registration.RelativePath}");
                summary.Append(registration.UsesLocalPath
                    ? " as a submodule using the local repository path."
                    : " as a submodule using remote.origin.url.");

                if (!string.IsNullOrWhiteSpace(registration.InitialCommitMessage))
                {
                    summary.Append(' ');
                    summary.Append(registration.InitialCommitMessage);
                }

                result.Messages.Add(summary.ToString());
            }

            return result;
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

        private static void RemovePathsFromIndex(string rootRepositoryPath, IReadOnlyList<string> relativePaths)
        {
            if (relativePaths.Count == 0)
                return;

            ExecuteGitCommand(rootRepositoryPath, $"rm -r --cached --ignore-unmatch -- {JoinQuotedArguments(relativePaths)}", allowEmptyOutput: true);
        }

        private static void UpdateGitIndexWithSubmodules(string rootRepositoryPath, IReadOnlyList<SubmoduleRegistration> registrations)
        {
            if (registrations.Count == 0)
                return;

            var input = new StringBuilder();
            foreach (SubmoduleRegistration registration in registrations)
            {
                input.Append("160000 ");
                input.Append(registration.HeadCommit);
                input.Append('\t');
                input.Append(registration.RelativePath);
                input.AppendLine();
            }

            ExecuteGitCommand(rootRepositoryPath, "update-index --index-info", allowEmptyOutput: true, standardInput: input.ToString());
        }

        private static void WriteGitModules(string rootRepositoryPath, IReadOnlyList<SubmoduleRegistration> registrations)
        {
            string gitModulesPath = Path.Combine(rootRepositoryPath, ".gitmodules");
            Dictionary<string, GitModuleEntry> existingEntries = ReadGitModules(gitModulesPath);

            foreach (SubmoduleRegistration registration in registrations)
            {
                existingEntries[registration.Name] = new GitModuleEntry
                {
                    Name = registration.Name,
                    Path = registration.RelativePath,
                    Url = registration.Url
                };
            }

            var output = new StringBuilder();
            foreach (GitModuleEntry entry in existingEntries.Values.OrderBy(entry => entry.Path, StringComparer.Ordinal))
            {
                output.Append("[submodule \"");
                output.Append(entry.Name);
                output.AppendLine("\"]");
                output.Append("\tpath = ");
                output.AppendLine(entry.Path);
                output.Append("\turl = ");
                output.AppendLine(entry.Url);
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

        private static void TryAbsorbGitDirectories(string rootRepositoryPath, IReadOnlyList<string> relativePaths)
        {
            if (relativePaths.Count == 0)
                return;

            try
            {
                ExecuteGitCommand(rootRepositoryPath, $"submodule absorbgitdirs -- {JoinQuotedArguments(relativePaths)}", allowEmptyOutput: true);
            }
            catch
            {
            }
        }

        private static string EnsureRepositoryHasCommit(string repositoryRoot)
        {
            if (RepositoryHasCommit(repositoryRoot))
                return string.Empty;

            ExecuteGitCommand(repositoryRoot, "add -A", allowEmptyOutput: true);
            ExecuteGitCommand(repositoryRoot, "commit -m \"Initial package commit\" --allow-empty", allowEmptyOutput: true);
            string shortCommit = ExecuteGitCommand(repositoryRoot, "rev-parse --short HEAD").Trim();
            return $"Created initial commit {shortCommit}.";
        }

        private static bool RepositoryHasCommit(string repositoryRoot)
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

        private static string JoinQuotedArguments(IEnumerable<string> values)
        {
            return string.Join(" ", values.Select(value => $"\"{value}\""));
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

        public sealed class BatchRegistrationResult
        {
            public List<string> Messages { get; } = new List<string>();
        }

        private sealed class SubmoduleRegistration
        {
            public string RelativePath;
            public string Name;
            public string Url;
            public string HeadCommit;
            public string InitialCommitMessage;
            public bool UsesLocalPath;
        }

        private sealed class GitModuleEntry
        {
            public string Name;
            public string Path;
            public string Url;
        }
    }
}
