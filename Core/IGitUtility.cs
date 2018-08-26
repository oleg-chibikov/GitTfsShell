using System.Threading.Tasks;
using GitTfsShell.Data;
using JetBrains.Annotations;
using LibGit2Sharp;

namespace GitTfsShell.Core
{
    public interface IGitUtility
    {
        void AddCommentToExistingCommit([NotNull] GitInfo gitInfo, [NotNull] string message);

        bool BranchExists([NotNull] GitInfo gitInfo, [NotNull] string branchName);

        [NotNull]
        Branch CheckoutBranch([NotNull] GitInfo gitInfo, [NotNull] string branchName);

        void CommitChanges([NotNull] GitInfo gitInfo, [NotNull] string message);

        [ItemCanBeNull]
        [NotNull]
        Task<GitInfo> GetInfoAsync([NotNull] string directoryPath);

        void StageChanges([NotNull] GitInfo repo);
    }
}