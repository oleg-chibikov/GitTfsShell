using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Logging;
using Easy.MessageHub;
using GitTfsShell.Data;
using GitTfsShell.Properties;
using JetBrains.Annotations;
using Microsoft.TeamFoundation.Framework.Client;
using Microsoft.TeamFoundation.Framework.Common;
using Microsoft.TeamFoundation.VersionControl.Client;
using Scar.Common.Messages;

namespace GitTfsShell.Core
{
    [UsedImplicitly]
    internal sealed class TfsUtility : ITfsUtility
    {
        [NotNull]
        private readonly ICmdUtility _cmdUtility;

        [NotNull]
        private readonly IIdentityManagementService _identityManagementService;

        [NotNull]
        private readonly ILog _logger;

        [NotNull]
        private readonly IMessageHub _messageHub;

        [NotNull]
        private readonly ICollection<UserInfo> _users;

        [NotNull]
        private readonly VersionControlServer _versionControlServer;

        public TfsUtility(
            [NotNull] VersionControlServer versionControlServer,
            [NotNull] ICmdUtility cmdUtility,
            [NotNull] IIdentityManagementService identityManagementService,
            [NotNull] IMessageHub messageHub,
            [NotNull] ILog logger)
        {
            _versionControlServer = versionControlServer ?? throw new ArgumentNullException(nameof(versionControlServer));
            _messageHub = messageHub ?? throw new ArgumentNullException(nameof(messageHub));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cmdUtility = cmdUtility;
            _identityManagementService = identityManagementService;
            _users = GetAllUsers();
        }

        public async Task ExecuteWithDisabledWorkspace(TfsInfo tfsInfo, string executable, string command, string directoryPath, CancellationToken cancellationToken)
        {
            _ = tfsInfo ?? throw new ArgumentNullException(nameof(tfsInfo));
            _ = executable ?? throw new ArgumentNullException(nameof(executable));
            _ = command ?? throw new ArgumentNullException(nameof(command));
            _ = directoryPath ?? throw new ArgumentNullException(nameof(directoryPath));

            var mapping = new WorkingFolder(tfsInfo.MappedServerFolder, directoryPath);

            try
            {
                _messageHub.Publish("Deleting TFS mapping...".ToMessage());
                tfsInfo.Workspace.DeleteMapping(mapping);
                await _cmdUtility.ExecuteCommandAsync(executable, command, directoryPath, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _messageHub.Publish("Restoring TFS mapping...".ToMessage());

                await RestoreMappingAsync(tfsInfo, directoryPath, mapping).ConfigureAwait(false);
            }
        }

        public string GetCurrentUser()
        {
            return _versionControlServer.AuthorizedUser;
        }

        public async Task<TfsInfo> GetInfoAsync(string directoryPath)
        {
            _ = directoryPath ?? throw new ArgumentNullException(nameof(directoryPath));

            return await Task.Run(
                    () =>
                    {
                        _logger.Trace("Getting TFS info...");

                        var workspace = _versionControlServer.TryGetWorkspace(directoryPath);
                        if (workspace == null)
                        {
                            _messageHub.Publish(new Message(new Exception("Not a TFS workspace"))); // Should be an exception to show a message as a popup (message list still not available)
                            return null;
                        }

                        var tfsMappedServerFolder = workspace.GetServerItemForLocalItem(directoryPath);
                        var teamProject = _versionControlServer.TryGetTeamProjectForServerPath(tfsMappedServerFolder);
                        var tfsWorkspaceName = workspace.Name;
                        var tfsInfo = new TfsInfo(workspace, tfsWorkspaceName, tfsMappedServerFolder, teamProject?.Name);
                        _logger.Debug("Got TFS info");
                        return tfsInfo;
                    })
                .ConfigureAwait(false);
        }

        public void GetLatest(TfsInfo tfsInfo)
        {
            _ = tfsInfo ?? throw new ArgumentNullException(nameof(tfsInfo));

            _messageHub.Publish("Getting TFS latest version...".ToMessage());

            // C:\Program Files (x86)\Microsoft Visual Studio 14.0\Common7\IDE\tf.exe
            // await _cmdUtility.ExecuteCommandAsync(Settings.Default.TfPath, $"get \"{tfsInfo.MappedServerFolder}\" /recursive", directoryPath).ConfigureAwait(false);
            tfsInfo.Workspace.Get(new GetRequest(tfsInfo.MappedServerFolder, RecursionType.Full, VersionSpec.Latest), GetOptions.Overwrite);
            _messageHub.Publish("Got TFS latest version".ToSuccess());
        }

        public string GetShelvesetQualifiedName(string user, string shelvesetName)
        {
            _ = shelvesetName ?? throw new ArgumentNullException(nameof(shelvesetName));

            _logger.TraceFormat("Getting shelveset qualified name for {0}...", shelvesetName);
            var shelveset = _versionControlServer.QueryShelvesets(shelvesetName, user).SingleOrDefault();
            var name = shelveset?.QualifiedName;
            _logger.DebugFormat("Got shelveset qualified name for {0}: {1}", shelvesetName, name);
            return name;
        }

        [NotNull]
        public string GetShelvesetUrl(ShelvesetData data, TfsInfo tfsInfo)
        {
            _ = data ?? throw new ArgumentNullException(nameof(data));

            var teamProjectName = tfsInfo?.TeamProjectName;
            return
                $"{Settings.Default.TfsUri}{(teamProjectName == null ? null : teamProjectName + "/")}_versionControl/shelveset?ss={GetShelvesetQualifiedName(data.User, data.Name)}";
        }

        public ICollection<string> GetShelvesets(string user)
        {
            _logger.TraceFormat("Getting shelvesets for {0}...", user);
            var shelvesets = _versionControlServer.QueryShelvesets(null, user).OrderByDescending(x => x.CreationDate).Select(x => x.Name).ToArray();
            _logger.DebugFormat("Got {0} shelvesets for {1}", shelvesets.Length, user);
            return shelvesets;
        }

        public ICollection<UserInfo> GetUsers(string searchPattern)
        {
            _ = searchPattern ?? throw new ArgumentNullException(nameof(searchPattern));

            _logger.TraceFormat("Getting users by {0}...", searchPattern);
            var users = _users.Where(
                    x => x.Name.IndexOf(searchPattern, StringComparison.OrdinalIgnoreCase) != -1 || x.Code.IndexOf(searchPattern, StringComparison.OrdinalIgnoreCase) != -1)
                .ToArray();
            _logger.DebugFormat("Got {0} users by {1}", users.Length, searchPattern);
            return users;
        }

        public bool ShelvesetExists(string user, string shelvesetName)
        {
            _ = shelvesetName ?? throw new ArgumentNullException(nameof(shelvesetName));

            _logger.TraceFormat("Checking that shelveset {0} exists...", shelvesetName);
            var exists = _versionControlServer.QueryShelvesets(shelvesetName, user).Any();
            _logger.DebugFormat("Shelveset {0} exists: {1}", shelvesetName, exists);
            return exists;
        }

        private void DeleteTempWorkspace(WorkingFolder mapping)
        {
            try
            {
                var user = GetCurrentUser();
                var workspaces = _versionControlServer.QueryWorkspaces(null, user, Environment.MachineName)
                    .Where(workspace => workspace.Name.StartsWith("git-tfs-", StringComparison.OrdinalIgnoreCase));
                foreach (var workspace in workspaces.Where(workspace => workspace.IsServerPathMapped(mapping.ServerItem)))
                {
                    _messageHub.Publish($"Temp workspace {workspace.Name} still exists. Deleting...".ToMessage());
                    workspace.DeleteMapping(mapping);
                    workspace.Delete();
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
                _messageHub.Publish("Cannot delete temp workspace...".ToWarning());
            }
        }

        [NotNull]
        private ICollection<UserInfo> GetAllUsers()
        {
            // Read "everyone" group
            var group = _identityManagementService.ReadIdentity(GroupWellKnownDescriptors.EveryoneGroup, MembershipQuery.Expanded, ReadIdentityOptions.None);

            // Read identities of that groups members
            var resultIdentities = _identityManagementService.ReadIdentities(group.Members, MembershipQuery.Direct, ReadIdentityOptions.DoNotQualifyAccountNames);

            // Filter machine accounts
            var validLocalUsers = resultIdentities.Where(
                    identity => identity.Descriptor.IdentityType == "System.Security.Principal.WindowsIdentity" && identity.IsActive && identity.IsContainer == false)
                .ToArray();
            return validLocalUsers.Select(x => new UserInfo(x.DisplayName, x.UniqueName)).OrderBy(x => x.DisplayName).ToArray();
        }

        private async Task RestoreMappingAsync(TfsInfo tfsInfo, string directoryPath, WorkingFolder mapping)
        {
            DeleteTempWorkspace(mapping);
            var restored = false;
            var attempt = 0;
            const int maxAttempts = 10;
            while (true)
            {
                try
                {
                    tfsInfo.Workspace.CreateMapping(mapping);
                    _messageHub.Publish("Successfully restored TFS mapping".ToSuccess());
                    _versionControlServer.GetWorkspace(directoryPath); // to verify that workspace was created
                    restored = true;
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex);
                    _messageHub.Publish($"Cannot restore TFS mapping. Attempt {attempt++} of {maxAttempts}...".ToWarning());
                    if (attempt <= maxAttempts)
                    {
                        await Task.Delay(3000).ConfigureAwait(false);
                        DeleteTempWorkspace(mapping);
                    }
                    else
                    {
                        break;
                    }
                }
            }

            if (!restored)
            {
                _messageHub.Publish("TFS mapping cannot be restored! Please restore it manually (Visual Studio - Team Explorer - Workspaces - Manage Workspaces)".ToError());
            }
        }
    }
}