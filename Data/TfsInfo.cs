using System;
using JetBrains.Annotations;
using Microsoft.TeamFoundation.VersionControl.Client;

namespace GitTfsShell.Data
{
    public sealed class TfsInfo
    {
        public TfsInfo([NotNull] Workspace workspace, [NotNull] string tfsWorkspaceName, [NotNull] string mappedServerFolder)
        {
            Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            WorkspaceName = tfsWorkspaceName ?? throw new ArgumentNullException(nameof(tfsWorkspaceName));
            MappedServerFolder = mappedServerFolder ?? throw new ArgumentNullException(nameof(mappedServerFolder));
        }

        [NotNull]
        public string MappedServerFolder { get; }

        [NotNull]
        public Workspace Workspace { get; }

        [NotNull]
        public string WorkspaceName { get; }
    }
}