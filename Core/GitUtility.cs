using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Common.Logging;
using Easy.MessageHub;
using GitTfsShell.Data;
using JetBrains.Annotations;
using LibGit2Sharp;
using Scar.Common.Messages;

namespace GitTfsShell.Core
{
    [UsedImplicitly]
    internal sealed class GitUtility : IGitUtility
    {
        [NotNull]
        private readonly ILog _logger;

        [NotNull]
        private readonly IMessageHub _messageHub;

        public GitUtility([NotNull] IMessageHub messageHub, [NotNull] ILog logger)
        {
            _messageHub = messageHub ?? throw new ArgumentNullException(nameof(messageHub));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void AddCommentToExistingCommit(GitInfo gitInfo, string message)
        {
            _ = gitInfo ?? throw new ArgumentNullException(nameof(gitInfo));
            _ = message ?? throw new ArgumentNullException(nameof(message));

            _messageHub.Publish($"Adding {message} to the commit message...".ToMessage());
            var lastCommit = gitInfo.Branch.Tip;
            try
            {
                gitInfo.Repo.Refs.RewriteHistory(
                    new RewriteHistoryOptions
                    {
                        BackupRefsNamespace = Guid.NewGuid().ToString(),
                        OnError = exception =>
                        {
                            _messageHub.Publish(exception);
                            _messageHub.Publish("Cannot rewrite comment".ToWarning());
                        },
                        OnSucceeding = () => _messageHub.Publish("Successfully rewritten last commit message".ToMessage()),
                        CommitHeaderRewriter = c =>
                        {
                            if (c.Message.Contains(message))
                            {
                                message = c.Message;
                            }
                            else
                            {
                                if (c.Message.EndsWith("\n", StringComparison.OrdinalIgnoreCase))
                                {
                                    message = c.Message + message;
                                }
                                else
                                {
                                    message = c.Message + "\n" + message;
                                }
                            }

                            return CommitRewriteInfo.From(c, message);
                        }
                    },
                    lastCommit);
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
            }
        }

        public bool BranchExists(GitInfo gitInfo, string branchName)
        {
            _ = gitInfo ?? throw new ArgumentNullException(nameof(gitInfo));
            _ = branchName ?? throw new ArgumentNullException(nameof(branchName));

            _logger.TraceFormat("Checking branch {0} exists...", branchName);
            var branchExists = gitInfo.Repo.Branches.Any(x => x.FriendlyName == branchName);
            _logger.DebugFormat("Branch {0} exists: {1}", branchName, branchExists);
            return branchExists;
        }

        public Branch CheckoutBranch(GitInfo gitInfo, string branchName)
        {
            _ = gitInfo ?? throw new ArgumentNullException(nameof(gitInfo));
            _ = branchName ?? throw new ArgumentNullException(nameof(branchName));

            _messageHub.Publish($"Checking branch {branchName} out...".ToMessage());
            var branch = gitInfo.Repo.Branches[branchName];
            var result = Commands.Checkout(gitInfo.Repo, branch);
            _messageHub.Publish($"Branch {branchName} is checked out".ToSuccess());
            return result;
        }

        public void CommitChanges(GitInfo gitInfo, string message)
        {
            _ = gitInfo ?? throw new ArgumentNullException(nameof(gitInfo));
            _ = message ?? throw new ArgumentNullException(nameof(message));

            _messageHub.Publish("Committing changes...".ToMessage());
            var signature = gitInfo.Repo.Config.BuildSignature(DateTimeOffset.Now);
            gitInfo.Repo.Commit(message, signature, signature);
            _messageHub.Publish("Changes are committed".ToSuccess());
        }

        public async Task<GitInfo> GetInfoAsync(string directoryPath)
        {
            _ = directoryPath ?? throw new ArgumentNullException(nameof(directoryPath));

            return await Task.Run(
                    () =>
                    {
                        _logger.Trace("Getting Git info...");
                        if (!Repository.IsValid(directoryPath))
                        {
                            _logger.Debug("Not a Git repository...");
                            return null;
                        }

                        var repo = new Repository(directoryPath);

                        var branches = repo.Branches.Where(x => !x.IsRemote).OrderByDescending(x => x.Tip.Author.When).ToList();
                        var status = repo.RetrieveStatus();
                        var isDirty = status.IsDirty;
                        var branch = branches.Single(x => x.CanonicalName == repo.Head.CanonicalName);
                        var uncommittedFilesCount = 0;
                        string[] commitMessages;
                        if (branch.CanonicalName != "master")
                        {
                            _logger.Trace("Calculating commits count...");
                            uncommittedFilesCount = status.Added.Count() + status.Modified.Count() + status.Removed.Count();
                            _logger.Trace("Getting commits messages...");
                            commitMessages = GetCommitMessagesFromBranchAsync(branch, repo).ToArray();
                            _logger.Debug("Got commit messages and count");
                        }
                        else
                        {
                            commitMessages = new string[0];
                        }

                        // var master = repo.Branches["master"];
                        // var nonMergeCommits = GetCommitsDiff(repo, master)
                        // .Where(x => x.Parents.Count() == 1)
                        // .Where(
                        // x =>
                        // {
                        // var branches = ListBranchesContainingCommit(repo, x.Sha).ToArray();
                        // return branches.Length == 1 && branches.Single().CanonicalName == repo.Head.CanonicalName;
                        // })
                        // .ToArray();
                        // commitMessages = nonMergeCommits.Select(x => BeautifyMessage(x.Message)).Distinct().ToArray();
                        // nonMergeBranchCommitsCount = nonMergeCommits.Length;
                        // }
                        // else
                        // {
                        // nonMergeBranchCommitsCount = repo.Head.Commits.Count();
                        // commitMessages = new[]
                        // {
                        // BeautifyMessage(repo.Head.Tip.Message)
                        // };
                        // }

                        var conflictsCount = repo.Index.Conflicts.Count();
                        var gitInfo = new GitInfo(repo, commitMessages.Distinct().ToArray(), branch, uncommittedFilesCount, isDirty, commitMessages.Length, conflictsCount, branches);

                        _logger.Debug("Got Git info");
                        return gitInfo;
                    })
                .ConfigureAwait(false);
        }

        public void StageChanges(GitInfo gitInfo)
        {
            _ = gitInfo ?? throw new ArgumentNullException(nameof(gitInfo));

            _messageHub.Publish("Staging changes...".ToMessage());
            Commands.Stage(gitInfo.Repo, "*");
            _messageHub.Publish("Changes are staged".ToSuccess());
        }

        [NotNull]
        private static string BeautifyMessage([NotNull] string message)
        {
            return message.Trim('\n').Replace("\n", Environment.NewLine);
        }

        [ItemNotNull]
        private static IEnumerable<string> GetCommitMessagesFromBranchAsync([NotNull] Branch branch, [NotNull] IRepository repo)
        {
            foreach (var e in repo.Refs.Log(branch.CanonicalName))
            {
                if (!e.Message.StartsWith("commit: ", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                yield return BeautifyMessage(e.Message.Substring(8));
            }
        }

        // [NotNull]
        // private IEnumerable<Branch> ListBranchesContainingCommit([NotNull] IRepository repo, [NotNull] string commitSha)
        // {
        // var localHeads = repo.Refs.Where(r => r.IsLocalBranch);

        // var commit = repo.Lookup<Commit>(commitSha);
        // var localHeadsContainingTheCommit = repo.Refs.ReachableFrom(
        // localHeads,
        // new[]
        // {
        // commit
        // });

        // return localHeadsContainingTheCommit.Select(branchRef => repo.Branches[branchRef.CanonicalName]);
        // }

        // private static ICommitLog GetCommitsDiff([NotNull] IRepository repo, [NotNull] Branch master)
        // {
        // if (repo == null)
        // {
        // throw new ArgumentNullException(nameof(repo));
        // }

        // return repo.Commits.QueryBy(
        // new CommitFilter
        // {
        // ExcludeReachableFrom = master, // formerly "Since"
        // IncludeReachableFrom = repo.Head // formerly "Until"
        // });
        // }
    }
}