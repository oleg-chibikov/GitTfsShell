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
            int nonMergeBranchCommitsCount,
            int conflictsCount)
        {
            Repo = repo ?? throw new ArgumentNullException(nameof(repo));
            CommitMessages = commitMessages ?? throw new ArgumentNullException(nameof(commitMessages));
            BranchName = branchName ?? throw new ArgumentNullException(nameof(branchName));
            UncommittedFilesCount = uncommittedFilesCount;
            IsDirty = isDirty;
            NonMergeBranchCommitsCount = nonMergeBranchCommitsCount;
            ConflictsCount = conflictsCount;
        }

        [NotNull]
        public string BranchName { get; }

        [CanBeNull]
        public string CommitMessage => CommitMessages.LastOrDefault(); //The first message is usually the most valuable - the later messages tend to be 'Fixed that'-like.

        [NotNull]
        public ICollection<string> CommitMessages { get; }

        public bool IsDirty { get; }

        public int ConflictsCount { get; }

        public int NonMergeBranchCommitsCount { get; }

        [NotNull]
        public IRepository Repo { get; }

        public int UncommittedFilesCount { get; }
    }
}