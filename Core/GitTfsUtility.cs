using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Easy.MessageHub;
using GitTfsShell.Data;
using GitTfsShell.Properties;
using JetBrains.Annotations;
using Scar.Common.Messages;

namespace GitTfsShell.Core
{
    [UsedImplicitly]
    internal sealed class GitTfsUtility : IGitTfsUtility
    {
        [NotNull]
        private static readonly string GitTfsPath = Path.Combine(Environment.CurrentDirectory, "gittfs", "git-tfs.exe");

        [NotNull]
        private readonly ICmdUtility _cmdUtility;

        [NotNull]
        private readonly IMessageHub _messageHub;

        [NotNull]
        private readonly ITfsUtility _tfsUtility;

        public GitTfsUtility([NotNull] IMessageHub messageHub, [NotNull] ITfsUtility tfsUtility, [NotNull] ICmdUtility cmdUtility)
        {
            _messageHub = messageHub ?? throw new ArgumentNullException(nameof(messageHub));
            _tfsUtility = tfsUtility ?? throw new ArgumentNullException(nameof(tfsUtility));
            _cmdUtility = cmdUtility ?? throw new ArgumentNullException(nameof(cmdUtility));
        }

        public async Task CloneAsync(TfsInfo tfsInfo, string directoryPath, CancellationToken cancellationToken)
        {
            _ = tfsInfo ?? throw new ArgumentNullException(nameof(tfsInfo));
            _ = directoryPath ?? throw new ArgumentNullException(nameof(directoryPath));

            _messageHub.Publish("Creating temp directory...");
            var tempDirectoryPath = CreateTempRepoDirectory(directoryPath);
            _messageHub.Publish($"Temp directory is '{tempDirectoryPath}'".ToMessage());

            await _cmdUtility.ExecuteCommandAsync(GitTfsPath, $"clone \"{Settings.Default.TfsUri}\" \"{tfsInfo.MappedServerFolder}\"", tempDirectoryPath, cancellationToken)
                .ConfigureAwait(false);
            var tempWorkspacePath = Directory.GetDirectories(tempDirectoryPath).Single(); // git-tfs should create one directory and place everything inside it
            _messageHub.Publish($"Temp workspace directory is '{tempWorkspacePath}'".ToMessage());
            try
            {
                await _cmdUtility.ExecuteCommandAsync(GitTfsPath, "cleanup", tempWorkspacePath, cancellationToken).ConfigureAwait(false);
                await _cmdUtility.ExecuteCommandAsync("cmd", "/c git gc", tempWorkspacePath, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                _messageHub.Publish("Cannot cleanup".ToWarning());
            }

            var finalGitPath = Path.Combine(directoryPath, ".git");
            if (Directory.Exists(finalGitPath))
            {
                _messageHub.Publish($"Deleting '{finalGitPath}'...".ToMessage());
                Directory.Delete(finalGitPath);
            }

            _messageHub.Publish($"Moving '.git' folder from temp directory '{tempWorkspacePath}' to the permanent one '{finalGitPath}'...".ToMessage());
            var tempGitDirectoryPath = Path.Combine(tempWorkspacePath, ".git");
            Directory.Move(tempGitDirectoryPath, finalGitPath);
            _messageHub.Publish($"Deleting temp directory '{tempDirectoryPath}'...".ToMessage());
            try
            {
                Directory.Delete(tempDirectoryPath, true);
            }
            catch
            {
                _messageHub.Publish($"Cannot delete temp directory '{tempDirectoryPath}'. Please delete it manually".ToWarning());
            }

            _messageHub.Publish($"Repository is cloned to '{directoryPath}'!".ToSuccess());
        }

        public async Task PullAsync(TfsInfo tfsInfo, string directoryPath, CancellationToken cancellationToken)
        {
            _ = tfsInfo ?? throw new ArgumentNullException(nameof(tfsInfo));
            _ = directoryPath ?? throw new ArgumentNullException(nameof(directoryPath));

            await _tfsUtility.ExecuteWithDisabledWorkspace(tfsInfo, GitTfsPath, "pull", directoryPath, cancellationToken).ConfigureAwait(false);
        }

        public async Task ShelveAsync(TfsInfo tfsInfo, string directoryPath, string shelvesetName, string comment, CancellationToken cancellationToken)
        {
            _ = tfsInfo ?? throw new ArgumentNullException(nameof(tfsInfo));
            _ = directoryPath ?? throw new ArgumentNullException(nameof(directoryPath));
            _ = shelvesetName ?? throw new ArgumentNullException(nameof(shelvesetName));
            _ = comment ?? throw new ArgumentNullException(nameof(comment));

            await _tfsUtility.ExecuteWithDisabledWorkspace(tfsInfo, GitTfsPath, $"shelve \"{shelvesetName}\" --force --comment \"{comment}\"", directoryPath, cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task CheckinAsync(TfsInfo tfsInfo, string directoryPath, string comment, CancellationToken cancellationToken)
        {
            _ = tfsInfo ?? throw new ArgumentNullException(nameof(tfsInfo));
            _ = directoryPath ?? throw new ArgumentNullException(nameof(directoryPath));
            _ = comment ?? throw new ArgumentNullException(nameof(comment));

            await _tfsUtility.ExecuteWithDisabledWorkspace(tfsInfo, GitTfsPath, $"checkin --force --comment=\"{comment}\"", directoryPath, cancellationToken).ConfigureAwait(false);
        }

        public async Task UnshelveAsync(TfsInfo tfsInfo, string directoryPath, string shelvesetName, string branchName, string user, CancellationToken cancellationToken)
        {
            _ = tfsInfo ?? throw new ArgumentNullException(nameof(tfsInfo));
            _ = directoryPath ?? throw new ArgumentNullException(nameof(directoryPath));
            _ = shelvesetName ?? throw new ArgumentNullException(nameof(shelvesetName));
            _ = branchName ?? throw new ArgumentNullException(nameof(branchName));

            var userParam = user == null ? null : $" -u={user}";
            await _tfsUtility.ExecuteWithDisabledWorkspace(
                    tfsInfo,
                    GitTfsPath,
                    $"unshelve \"{shelvesetName}\" \"{branchName}\"{userParam} --force",
                    directoryPath,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        [NotNull]
        private static string CreateTempRepoDirectory([NotNull] string directoryPath)
        {
            var tempDirectoryPath = GetTempDirectoryPath(directoryPath);
            while (Directory.Exists(tempDirectoryPath))
            {
                tempDirectoryPath = GetTempDirectoryPath(directoryPath);
            }

            Directory.CreateDirectory(tempDirectoryPath);
            return tempDirectoryPath;
        }

        [NotNull]
        private static string GetTempDirectoryPath(string path)
        {
            return Path.Combine(Path.GetPathRoot(path) ?? throw new InvalidOperationException(), Path.GetRandomFileName());
        }
    }
}