using System.Threading;
using System.Threading.Tasks;
using GitTfsShell.Data;
using JetBrains.Annotations;

namespace GitTfsShell.Core
{
    public interface IGitTfsUtility
    {
        Task CloneAsync([NotNull] TfsInfo tfsInfo, [NotNull] string directoryPath, CancellationToken cancellationToken);

        Task PullAsync([NotNull] TfsInfo tfsInfo, [NotNull] string directoryPath, CancellationToken cancellationToken);

        Task ShelveAsync([NotNull] TfsInfo tfsInfo, [NotNull] string directoryPath, [NotNull] string shelvesetName, [NotNull] string comment, CancellationToken cancellationToken);

        Task UnshelveAsync(
            [NotNull] TfsInfo tfsInfo,
            [NotNull] string directoryPath,
            [NotNull] string shelvesetName,
            [NotNull] string branchName,
            [CanBeNull] string user,
            CancellationToken cancellationToken);
    }
}