using System.Threading;
using System.Threading.Tasks;
using GitTfsShell.Data;
using JetBrains.Annotations;

namespace GitTfsShell.Core
{
    public interface IGitTfsUtility
    {
        [NotNull]
        Task CloneAsync([NotNull] TfsInfo tfsInfo, [NotNull] string directoryPath, CancellationToken cancellationToken);

        [NotNull]
        Task PullAsync([NotNull] TfsInfo tfsInfo, [NotNull] string directoryPath, CancellationToken cancellationToken);

        [NotNull]
        Task ShelveAsync([NotNull] TfsInfo tfsInfo, [NotNull] string directoryPath, [NotNull] string shelvesetName, [NotNull] string comment, CancellationToken cancellationToken);

        [NotNull]
        Task CheckinAsync([NotNull] TfsInfo tfsInfo, [NotNull] string directoryPath, [NotNull] string comment, CancellationToken cancellationToken);

        [NotNull]
        Task UnshelveAsync(
            [NotNull] TfsInfo tfsInfo,
            [NotNull] string directoryPath,
            [NotNull] string shelvesetName,
            [NotNull] string branchName,
            [CanBeNull] string user,
            CancellationToken cancellationToken);
    }
}