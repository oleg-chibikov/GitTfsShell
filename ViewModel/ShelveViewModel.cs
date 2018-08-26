using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Windows;
using Easy.MessageHub;
using GitTfsShell.Core;
using GitTfsShell.Data;
using GitTfsShell.Properties;
using JetBrains.Annotations;
using PropertyChanged;
using Scar.Common.Messages;
using Scar.Common.WPF.Commands;

namespace GitTfsShell.ViewModel
{
    [AddINotifyPropertyChangedInterface]
    [UsedImplicitly]
    public sealed class ShelveViewModel : IDisposable, IDataErrorInfo
    {
        [NotNull]
        private readonly ICmdUtility _cmdUtility;

        [NotNull]
        private readonly string _directoryPath;

        [NotNull]
        private readonly IDictionary<string, string> _errors = new Dictionary<string, string>();

        [NotNull]
        private readonly GitInfo _gitInfo;

        [NotNull]
        private readonly IGitTfsUtility _gitTfsUtility;

        [NotNull]
        private readonly IGitUtility _gitUtility;

        [NotNull]
        private readonly IMessageHub _messageHub;

        [NotNull]
        private readonly IList<Guid> _subscriptionTokens = new List<Guid>();

        [NotNull]
        private readonly SynchronizationContext _synchronizationContext;

        [NotNull]
        private readonly TfsInfo _tfsInfo;

        [NotNull]
        private readonly ITfsUtility _tfsUtility;

        private bool _commitDirty;

        private bool _hasValidationErrors;

        private bool _isLoading;

        public ShelveViewModel(
            [NotNull] string directoryPath,
            [NotNull] GitInfo gitInfo,
            [NotNull] TfsInfo tfsInfo,
            [NotNull] IMessageHub messageHub,
            [NotNull] IGitUtility gitUtility,
            [NotNull] ICmdUtility cmdUtility,
            [NotNull] IGitTfsUtility gitTfsUtility,
            [NotNull] ITfsUtility tfsUtility,
            [NotNull] SynchronizationContext synchronizationContext)
        {
            _messageHub = messageHub ?? throw new ArgumentNullException(nameof(messageHub));
            _gitUtility = gitUtility ?? throw new ArgumentNullException(nameof(gitUtility));
            _cmdUtility = cmdUtility ?? throw new ArgumentNullException(nameof(cmdUtility));
            _gitTfsUtility = gitTfsUtility ?? throw new ArgumentNullException(nameof(gitTfsUtility));
            _tfsUtility = tfsUtility ?? throw new ArgumentNullException(nameof(tfsUtility));
            _synchronizationContext = synchronizationContext ?? throw new ArgumentNullException(nameof(synchronizationContext));
            _gitInfo = gitInfo ?? throw new ArgumentNullException(nameof(gitInfo));
            _tfsInfo = tfsInfo ?? throw new ArgumentNullException(nameof(tfsInfo));

            ShelveCommand = new CorrelationCommand(Shelve, () => CanExecute);
            CancelCommand = new CorrelationCommand(Cancel, () => !IsLoading);

            _directoryPath = directoryPath ?? throw new ArgumentNullException(nameof(directoryPath));
            IsDirty = CommitDirty = _gitInfo.IsDirty;
            ShelvesetName = GetShelvesetName();
            CommitMessage = _gitInfo.CommitMessage ?? string.Empty;
            CommitMessages = _gitInfo.CommitMessages;
            _messageHub.Publish(DialogType.Shelve);
            _subscriptionTokens.Add(messageHub.Subscribe<TaskState>(OnTaskAction));
        }

        [NotNull]
        public IRefreshableCommand CancelCommand { get; }

        public bool CommitDirty
        {
            get => _commitDirty;
            set
            {
                var oldShelvesetName = ShelvesetName;
                var isDefaultShelvesetName = oldShelvesetName == GetShelvesetName();

                _commitDirty = value;
                if (isDefaultShelvesetName)
                {
                    ShelvesetName = GetShelvesetName();
                }
            }
        }

        [NotNull]
        public string CommitMessage { get; set; }

        [NotNull]
        public ICollection<string> CommitMessages { get; }

        public string Error => throw new NotSupportedException();

        public bool IsDirty { get; }

        [NotNull]
        public IRefreshableCommand ShelveCommand { get; }

        [NotNull]
        public string ShelvesetName { get; set; }

        private bool CanExecute => !HasValidationErrors && !IsLoading;

        private bool HasValidationErrors
        {
            get => _hasValidationErrors;
            set
            {
                _hasValidationErrors = value;
                ShelveCommand.RaiseCanExecuteChanged();
            }
        }

        private bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                ShelveCommand.RaiseCanExecuteChanged();
                CancelCommand.RaiseCanExecuteChanged();
            }
        }

        [CanBeNull]
        public string this[[NotNull] string columnName]
        {
            get
            {
                string errorMsg = null;
                switch (columnName)
                {
                    case nameof(ShelvesetName):
                        if (string.IsNullOrWhiteSpace(ShelvesetName))
                        {
                            errorMsg = "ShelvesetName is required";
                        }

                        break;
                    case nameof(CommitMessage):
                        if (string.IsNullOrWhiteSpace(CommitMessage))
                        {
                            errorMsg = "CommitMessage is required";
                        }

                        break;
                }

                if (errorMsg != null)
                {
                    _errors[columnName] = errorMsg;
                }
                else
                {
                    _errors.Remove(columnName);
                }

                HasValidationErrors = _errors.Any();
                return errorMsg;
            }
        }

        public void Dispose()
        {
            foreach (var subscriptionToken in _subscriptionTokens)
            {
                _messageHub.Unsubscribe(subscriptionToken);
            }

            _subscriptionTokens.Clear();
            _gitInfo.Repo.Dispose();
        }

        private static MessageBoxResult ConfirmOverwriteExistingShelveset([NotNull] string shelvesetName, [NotNull] string user)
        {
            var confirmationResult = MessageBox.Show(
                $"Shelveset {shelvesetName} already exists for user {user}. Overwrite?",
                "Shelveset exists",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            return confirmationResult;
        }

        private void Cancel()
        {
            _messageHub.Publish(DialogType.None);
        }

        [NotNull]
        private string GetShelvesetName()
        {
            var commitCount = _gitInfo.NonMergeBranchCommitsCount;
            if (IsDirty && CommitDirty)
            {
                commitCount++;
            }

            return string.Format(Settings.Default.ShelvesetTemplate, _gitInfo.BranchName, commitCount);
        }

        private void OnTaskAction(TaskState taskState)
        {
            switch (taskState)
            {
                case TaskState.Started:
                    _synchronizationContext.Send(x => IsLoading = true, null);
                    break;
                case TaskState.Error:
                case TaskState.Finished:
                    _synchronizationContext.Send(x => IsLoading = false, null);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(taskState));
            }
        }

        private async void Shelve()
        {
            if (!CanExecute)
            {
                throw new InvalidOperationException();
            }

            await _cmdUtility.ExecuteTaskAsync(
                    async cancellationToken =>
                    {
                        var shelvesetName = ShelvesetName;
                        var commitMessage = CommitMessage;

                        var user = _tfsUtility.GetCurrentUser();
                        var shelvesetExists = _tfsUtility.ShelvesetExists(user, shelvesetName);
                        if (shelvesetExists)
                        {
                            var confirmationResult = ConfirmOverwriteExistingShelveset(shelvesetName, user);
                            if (confirmationResult != MessageBoxResult.Yes)
                            {
                                return;
                            }
                        }

                        _messageHub.Publish(new ShelvesetData(shelvesetName));
                        _synchronizationContext.Post(
                            x =>
                            {
                                Clipboard.SetText(shelvesetName);
                                _messageHub.Publish("Shelveset name is copied to clipboard".ToMessage());
                            },
                            null);
                        _messageHub.Publish(DialogType.None);

                        if (CommitDirty)
                        {
                            _gitUtility.StageChanges(_gitInfo);
                            _gitUtility.CommitChanges(_gitInfo, commitMessage);
                        }

                        await _gitTfsUtility.ShelveAsync(_tfsInfo, _directoryPath, shelvesetName, commitMessage, cancellationToken).ConfigureAwait(false);
                        _gitUtility.AddCommentToExistingCommit(_gitInfo, shelvesetName);
                        _messageHub.Publish(await _gitUtility.GetInfoAsync(_directoryPath).ConfigureAwait(false));
                        _messageHub.Publish(new ShelvesetData(shelvesetName, user));
                    })
                .ConfigureAwait(false);
        }
    }
}