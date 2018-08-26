using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using LibGit2Sharp;

namespace GitTfsShell.Data
{
    public sealed class GitInfo
    {
        public GitInfo(
            [NotNull] IRepository repo,
            [NotNull] string[] commitMessages,
            [NotNull] string branchName,
            int uncommittedFilesCount,
            bool isDirty,
            int nonMergeBranchCommitsCount)
        {
            Repo = repo ?? throw new ArgumentNullException(nameof(repo));
            CommitMessages = commitMessages ?? throw new ArgumentNullException(nameof(commitMessages));
            BranchName = branchName ?? throw new ArgumentNullException(nameof(branchName));
            UncommittedFilesCount = uncommittedFilesCount;
            IsDirty = isDirty;
            NonMergeBranchCommitsCount = nonMergeBranchCommitsCount;
        }

        [NotNull]
        public string BranchName { get; }

        [CanBeNull]
        public string CommitMessage => CommitMessages.FirstOrDefault();

        [NotNull]
        public ICollection<string> CommitMessages { get; }

        public bool IsDirty { get; }

        public int NonMergeBranchCommitsCount { get; }

        [NotNull]
        public IRepository Repo { get; }

        public int UncommittedFilesCount { get; }
    }
}