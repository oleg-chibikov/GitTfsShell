using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GitTfsShell.Data;
using JetBrains.Annotations;

namespace GitTfsShell.Core
{
    public interface ITfsUtility
    {
        Task ExecuteWithDisabledWorkspace(
            [NotNull] TfsInfo tfsInfo,
            [NotNull] string executable,
            [NotNull] string command,
            [NotNull] string directoryPath,
            CancellationToken cancellationToken);

        [NotNull]
        string GetCurrentUser();

        [ItemCanBeNull]
        [NotNull]
        Task<TfsInfo> GetInfoAsync([NotNull] string directoryPath);

        void GetLatest([NotNull] TfsInfo tfsInfo);

        [CanBeNull]
        string GetShelvesetQualifiedName([CanBeNull] string user, [NotNull] string shelvesetName);

        [CanBeNull]
        string GetShelvesetUrl([NotNull] ShelvesetData data, [CanBeNull] TfsInfo tfsInfo);

        [NotNull]
        ICollection<string> GetShelvesets([CanBeNull] string user);

        [NotNull]
        ICollection<UserInfo> GetUsers([NotNull] string searchPattern);

        bool ShelvesetExists([CanBeNull] string user, [NotNull] string shelvesetName);
    }
}