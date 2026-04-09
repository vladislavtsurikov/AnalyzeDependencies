using System;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using VladislavTsurikov.AnalyzeDependencies.Editor.Core.Utilities;

namespace VladislavTsurikov.AnalyzeDependencies.Tests.Editor
{
    public class GitRepositoryUtilityTests
    {
        private string _tempDirectory;

        [SetUp]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "AnalyzeDependenciesTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }

        [Test]
        public void InitializeRepository_CreatesDotGitFolder()
        {
            if (!IsGitAvailable())
            {
                Assert.Ignore("git executable is not available in the current environment.");
            }

            GitRepositoryUtility.InitializeRepository(_tempDirectory);

            Assert.That(GitRepositoryUtility.HasGitRepository(_tempDirectory), Is.True);
        }

        [Test]
        public void HasGitRepository_ReturnsTrueForDotGitFile()
        {
            File.WriteAllText(Path.Combine(_tempDirectory, ".git"), "gitdir: ../.git/modules/test");

            Assert.That(GitRepositoryUtility.HasGitRepository(_tempDirectory), Is.True);
        }

        [Test]
        public void RemoveGitRepositoryMarker_DeletesDotGitFolder()
        {
            Directory.CreateDirectory(Path.Combine(_tempDirectory, ".git"));

            GitRepositoryUtility.RemoveGitRepositoryMarker(_tempDirectory);

            Assert.That(GitRepositoryUtility.HasGitRepository(_tempDirectory), Is.False);
        }

        [Test]
        public void RemoveGitRepositoryMarker_DeletesDotGitFile()
        {
            File.WriteAllText(Path.Combine(_tempDirectory, ".git"), "gitdir: ../.git/modules/test");

            GitRepositoryUtility.RemoveGitRepositoryMarker(_tempDirectory);

            Assert.That(File.Exists(Path.Combine(_tempDirectory, ".git")), Is.False);
        }

        [Test]
        public void RegisterSubmodule_CreatesInitialCommitWhenRepositoryHasNoHead()
        {
            if (!IsGitAvailable())
            {
                Assert.Ignore("git executable is not available in the current environment.");
            }

            string rootDirectory = Path.Combine(_tempDirectory, "Root");
            string submoduleDirectory = Path.Combine(rootDirectory, "Package");
            Directory.CreateDirectory(submoduleDirectory);

            GitRepositoryUtility.InitializeRepository(rootDirectory);
            ConfigureGitIdentity(rootDirectory);
            File.WriteAllText(Path.Combine(rootDirectory, "README.md"), "root");
            RunGit(rootDirectory, "add -A");
            RunGit(rootDirectory, "commit -m \"Initial root commit\"");

            GitRepositoryUtility.InitializeRepository(submoduleDirectory);
            ConfigureGitIdentity(submoduleDirectory);
            File.WriteAllText(Path.Combine(submoduleDirectory, "package.json"), "{ }");

            string result = GitRepositoryUtility.RegisterSubmodule(rootDirectory, submoduleDirectory);

            Assert.That(result, Does.Contain("Created initial commit"));
        }

        private static bool IsGitAvailable()
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                using (var process = new Process { StartInfo = startInfo })
                {
                    process.Start();
                    process.WaitForExit();
                    return process.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private static void ConfigureGitIdentity(string workingDirectory)
        {
            RunGit(workingDirectory, "config user.name \"Tests\"");
            RunGit(workingDirectory, "config user.email \"tests@example.com\"");
        }

        private static void RunGit(string workingDirectory, string arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory
            };

            using (var process = new Process { StartInfo = startInfo })
            {
                process.Start();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    Assert.Fail(error);
                }
            }
        }
    }
}
