using System;
using JetBrains.Annotations;
using Microsoft.TeamFoundation.VersionControl.Client;

namespace GitTfsShell.Data
{
    public sealed class TfsInfo
    {
        public TfsInfo([NotNull] Workspace workspace, [NotNull] string tfsWorkspaceName, [NotNull] string mappedServerFolder, [CanBeNull] string teamProjectName)
        {
            Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            WorkspaceName = tfsWorkspaceName ?? throw new ArgumentNullException(nameof(tfsWorkspaceName));
            MappedServerFolder = mappedServerFolder ?? throw new ArgumentNullException(nameof(mappedServerFolder));
            TeamProjectName = teamProjectName;
        }

        [NotNull]
        public string MappedServerFolder { get; }

        [NotNull]
        public Workspace Workspace { get; }

        [NotNull]
        public string WorkspaceName { get; }

        [CanBeNull]
        public string TeamProjectName { get; }
    }
}