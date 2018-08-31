using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Windows;
using Easy.MessageHub;
using GitTfsShell.Core;
using GitTfsShell.Data;
using JetBrains.Annotations;
using PropertyChanged;
using Scar.Common.WPF.Commands;

namespace GitTfsShell.ViewModel
{
    [AddINotifyPropertyChangedInterface]
    [UsedImplicitly]
    public sealed class UnshelveViewModel : IDisposable, IDataErrorInfo
    {
        [CanBeNull]
        private static string currentBranchName;

        [CanBeNull]
        private static string currentShelvesetName;

        [CanBeNull]
        private static UserInfo currentUser;

        [CanBeNull]
        private static string currentUsersSearchPattern;

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

        private bool _hasValidationErrors;

        private bool _isLoading;

        public UnshelveViewModel(
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

            UnshelveCommand = new CorrelationCommand(Unshelve, () => CanExecute);
            CancelCommand = new CorrelationCommand(Cancel, () => !IsLoading);

            if (User == null)
            {
                UsersSearchPattern = string.Empty; // sets current user
            }

            _directoryPath = directoryPath ?? throw new ArgumentNullException(nameof(directoryPath));
            _messageHub.Publish(DialogType.Unshelve);
            _subscriptionTokens.Add(messageHub.Subscribe<TaskState>(OnTaskAction));
        }

        [NotNull]
        public static ObservableCollection<UserInfo> Users { get; } = new ObservableCollection<UserInfo>();

        [NotNull]
        public static ObservableCollection<string> UserShelvesetNames { get; } = new ObservableCollection<string>();

        [CanBeNull]
        public string BranchName
        {
            get => currentBranchName;
            set => currentBranchName = value;
        }

        [NotNull]
        public IRefreshableCommand CancelCommand { get; }

        public string Error => throw new NotSupportedException();

        [CanBeNull]
        public string ShelvesetName
        {
            get => currentShelvesetName;
            set
            {
                currentShelvesetName = value;
                string prefix = null;
                if (User != null && User.Code != _tfsUtility.GetCurrentUser())
                {
                    prefix = User.Name + "_";
                }

                var branchName = prefix + value;
                branchName = branchName.Replace(" ", "_").Replace("/", "_");

                BranchName = branchName;
            }
        }

        [NotNull]
        public IRefreshableCommand UnshelveCommand { get; }

        [CanBeNull]
        public UserInfo User
        {
            get => currentUser;
            set
            {
                currentUser = value;
                if (value == null)
                {
                    return;
                }

                var shelvesets = _tfsUtility.GetShelvesets(value.Code);
                UserShelvesetNames.Clear();
                foreach (var shelveset in shelvesets)
                {
                    UserShelvesetNames.Add(shelveset);
                }

                ShelvesetName = shelvesets.FirstOrDefault();
            }
        }

        [CanBeNull]
        public string UsersSearchPattern
        {
            get => currentUsersSearchPattern;

            set
            {
                currentUsersSearchPattern = value;
                var text = value;
                if (string.IsNullOrWhiteSpace(text))
                {
                    text = _tfsUtility.GetCurrentUser();
                }

                var users = _tfsUtility.GetUsers(text);
                Users.Clear();
                foreach (var user in users)
                {
                    Users.Add(user);
                }

                User = users.FirstOrDefault();
            }
        }

        private bool CanExecute => !HasValidationErrors && !IsLoading;

        private bool HasValidationErrors
        {
            get => _hasValidationErrors;
            set
            {
                _hasValidationErrors = value;
                UnshelveCommand.RaiseCanExecuteChanged();
            }
        }

        private bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                UnshelveCommand.RaiseCanExecuteChanged();
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
                    case nameof(BranchName):
                        if (string.IsNullOrWhiteSpace(BranchName))
                        {
                            errorMsg = "BranchName is required";
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

        private static void WarnBranchExists([NotNull] string branchName)
        {
            MessageBox.Show($"Branch {branchName} already exists. Please choose another name", "Branch exists", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void Cancel()
        {
            _messageHub.Publish(DialogType.None);
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

        private async void Unshelve()
        {
            if (!CanExecute)
            {
                throw new InvalidOperationException();
            }

            await _cmdUtility.ExecuteTaskAsync(
                    async cancellationToken =>
                    {
                        BranchName = BranchName?.Replace(" ", "_");
                        var branchName = BranchName;
                        var shelvesetName = ShelvesetName;

                        var shelvesetExists = _tfsUtility.ShelvesetExists(null, shelvesetName ?? throw new InvalidOperationException());
                        if (!shelvesetExists)
                        {
                            MessageBox.Show(
                                $"Shelveset {shelvesetName} does not exist. Please select an existing one",
                                "Shelveset does not exist",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                            return;
                        }

                        var branchExists = _gitUtility.BranchExists(_gitInfo, branchName ?? throw new InvalidOperationException());
                        if (branchExists)
                        {
                            WarnBranchExists(branchName);
                            return;
                        }

                        if (User != null)
                        {
                            _messageHub.Publish(new ShelvesetData(shelvesetName, User.Code));
                        }

                        _messageHub.Publish(DialogType.None);

                        await _gitTfsUtility.UnshelveAsync(_tfsInfo, _directoryPath, shelvesetName, branchName, User?.Code, cancellationToken).ConfigureAwait(false);

                        _gitUtility.CheckoutBranch(_gitInfo, branchName);
                        _messageHub.Publish(await _gitUtility.GetInfoAsync(_directoryPath).ConfigureAwait(false));
                    })
                .ConfigureAwait(false);
        }
    }
}