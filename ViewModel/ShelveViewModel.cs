using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Easy.MessageHub;
using GitTfsShell.Core;
using GitTfsShell.Data;
using GitTfsShell.Properties;
using GitTfsShell.View;
using JetBrains.Annotations;
using PropertyChanged;
using Scar.Common.MVVM.Commands;
using Scar.Common.MVVM.ViewModel;

namespace GitTfsShell.ViewModel
{
    [AddINotifyPropertyChangedInterface]
    [UsedImplicitly]
    public sealed class ShelveViewModel : BaseViewModel, IDataErrorInfo
    {
        [NotNull]
        private readonly ICmdUtility _cmdUtility;

        [NotNull]
        private readonly Func<string, bool, ConfirmationViewModel> _confirmationViewModelFactory;

        [NotNull]
        private readonly Func<ConfirmationViewModel, IConfirmationWindow> _confirmationWindowFactory;

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
            [NotNull] SynchronizationContext synchronizationContext,
            [NotNull] Func<string, bool, ConfirmationViewModel> confirmationViewModelFactory,
            [NotNull] Func<ConfirmationViewModel, IConfirmationWindow> confirmationWindowFactory,
            [NotNull] ICommandManager commandManager)
            : base(commandManager)
        {
            _messageHub = messageHub ?? throw new ArgumentNullException(nameof(messageHub));
            _gitUtility = gitUtility ?? throw new ArgumentNullException(nameof(gitUtility));
            _cmdUtility = cmdUtility ?? throw new ArgumentNullException(nameof(cmdUtility));
            _gitTfsUtility = gitTfsUtility ?? throw new ArgumentNullException(nameof(gitTfsUtility));
            _tfsUtility = tfsUtility ?? throw new ArgumentNullException(nameof(tfsUtility));
            _synchronizationContext = synchronizationContext ?? throw new ArgumentNullException(nameof(synchronizationContext));
            _confirmationViewModelFactory = confirmationViewModelFactory ?? throw new ArgumentNullException(nameof(confirmationViewModelFactory));
            _confirmationWindowFactory = confirmationWindowFactory ?? throw new ArgumentNullException(nameof(confirmationWindowFactory));
            _gitInfo = gitInfo ?? throw new ArgumentNullException(nameof(gitInfo));
            _tfsInfo = tfsInfo ?? throw new ArgumentNullException(nameof(tfsInfo));
            _directoryPath = directoryPath ?? throw new ArgumentNullException(nameof(directoryPath));

            ShelveOrCheckinCommand = AddCommand(ShelveOrCheckin, () => CanExecute);
            CancelCommand = AddCommand(Cancel, () => !IsLoading);

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

        public bool CheckinInsteadOfShelving { get; set; }

        [DependsOn(nameof(CheckinInsteadOfShelving))]
        public bool IsShelvesetNameVisible => !CheckinInsteadOfShelving;

        [NotNull]
        public string CommitMessage { get; set; }

        [NotNull]
        public ICollection<string> CommitMessages { get; }

        public bool IsDirty { get; }

        [NotNull]
        public IRefreshableCommand ShelveOrCheckinCommand { get; }

        [NotNull]
        public string ShelvesetName { get; set; }

        private bool CanExecute => !HasValidationErrors && !IsLoading;

        private bool HasValidationErrors
        {
            get => _hasValidationErrors;
            set
            {
                _hasValidationErrors = value;
                ShelveOrCheckinCommand.RaiseCanExecuteChanged();
            }
        }

        private bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                ShelveOrCheckinCommand.RaiseCanExecuteChanged();
                CancelCommand.RaiseCanExecuteChanged();
            }
        }

        public string Error => throw new NotSupportedException();

        [CanBeNull]
        public string this[[NotNull] string columnName]
        {
            get
            {
                string errorMsg = null;
                switch (columnName)
                {
                    case nameof(ShelvesetName):
                        if (!CheckinInsteadOfShelving && string.IsNullOrWhiteSpace(ShelvesetName))
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

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                foreach (var subscriptionToken in _subscriptionTokens)
                {
                    _messageHub.Unsubscribe(subscriptionToken);
                }

                _subscriptionTokens.Clear();
                _gitInfo.Repo.Dispose();
            }
        }

        private Task<bool> ConfirmOverwriteExistingShelvesetAsync([NotNull] string shelvesetName, [NotNull] string user)
        {
            var confirmationViewModel = _confirmationViewModelFactory($"Shelveset {shelvesetName} already exists for user {user}. Overwrite?", true);

            _synchronizationContext.Send(
                x =>
                {
                    var confirmationWindow = _confirmationWindowFactory(confirmationViewModel);
                    confirmationWindow.ShowDialog();
                },
                null);
            return confirmationViewModel.UserInput;
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

        private async void ShelveOrCheckin()
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
                            var confirmationResult = await ConfirmOverwriteExistingShelvesetAsync(shelvesetName, user);
                            if (!confirmationResult)
                            {
                                return;
                            }
                        }

                        _messageHub.Publish(new ShelvesetData(shelvesetName));
                        _synchronizationContext.Post(
                            x => _cmdUtility.CopyToClipboard(shelvesetName),
                            null);
                        _messageHub.Publish(DialogType.None);

                        if (CommitDirty)
                        {
                            _gitUtility.StageChanges(_gitInfo);
                            _gitUtility.CommitChanges(_gitInfo, commitMessage);
                        }

                        if (!CheckinInsteadOfShelving)
                        {
                            await _gitTfsUtility.ShelveAsync(_tfsInfo, _directoryPath, shelvesetName, commitMessage, cancellationToken).ConfigureAwait(false);
                        }
                        else
                        {
                            await _gitTfsUtility.CheckinAsync(_tfsInfo, _directoryPath, commitMessage, cancellationToken).ConfigureAwait(false);
                        }

                        _gitUtility.AddCommentToExistingCommit(_gitInfo, CheckinInsteadOfShelving ? "Check-in" : shelvesetName);

                        _messageHub.Publish(await _gitUtility.GetInfoAsync(_directoryPath).ConfigureAwait(false));
                        _messageHub.Publish(new ShelvesetData(shelvesetName, user));
                    })
                .ConfigureAwait(false);
        }
    }
}