using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Easy.MessageHub;
using GitTfsShell.Core;
using GitTfsShell.Data;
using GitTfsShell.View;
using JetBrains.Annotations;
using PropertyChanged;
using Scar.Common;
using Scar.Common.MVVM.Commands;
using Scar.Common.MVVM.ViewModel;

namespace GitTfsShell.ViewModel
{
    [AddINotifyPropertyChangedInterface]
    [UsedImplicitly]
    public sealed class UnshelveViewModel : BaseViewModel, IDataErrorInfo
    {
        [CanBeNull]
        private static string _currentBranchName;

        [CanBeNull]
        private static string _currentShelvesetName;

        [CanBeNull]
        private static UserInfo _currentUser;

        [CanBeNull]
        private static string _currentUsersSearchPattern;

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

        [NotNull]
        private readonly IRateLimiter _rateLimiter;

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
            [NotNull] SynchronizationContext synchronizationContext,
            [NotNull] Func<string, bool, ConfirmationViewModel> confirmationViewModelFactory,
            [NotNull] Func<ConfirmationViewModel, IConfirmationWindow> confirmationWindowFactory,
            [NotNull] ICommandManager commandManager,
            [NotNull] IRateLimiter rateLimiter)
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
            _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
            _gitInfo = gitInfo ?? throw new ArgumentNullException(nameof(gitInfo));
            _tfsInfo = tfsInfo ?? throw new ArgumentNullException(nameof(tfsInfo));
            _directoryPath = directoryPath ?? throw new ArgumentNullException(nameof(directoryPath));

            UnshelveCommand = AddCommand(Unshelve, () => CanExecute);
            CancelCommand = AddCommand(Cancel, () => !IsLoading);
            OpenShelvesetInBrowserCommand = AddCommand(OpenShelvesetInBrowser, () => ShelvesetName != null);
            CopyShelvesetToClipboardCommand = AddCommand(CopyShelvesetToClipboard, () => ShelvesetName != null);

            if (User == null)
            {
                UsersSearchPattern = string.Empty; // sets current user
            }

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
            get => _currentBranchName;
            set => _currentBranchName = value;
        }

        [NotNull]
        public IRefreshableCommand CancelCommand { get; }

        [NotNull]
        public IRefreshableCommand OpenShelvesetInBrowserCommand { get; }

        [NotNull]
        public IRefreshableCommand CopyShelvesetToClipboardCommand { get; }

        [CanBeNull]
        public string ShelvesetName
        {
            get => _currentShelvesetName;
            set
            {
                _currentShelvesetName = value;
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
            get => _currentUser;
            set
            {
                _currentUser = value;
                if (value == null)
                {
                    return;
                }
                UserShelvesetNames.Clear();

                var shelvesets = _tfsUtility.GetShelvesets(value.Code);
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
            get => _currentUsersSearchPattern;

            set
            {
                _currentUsersSearchPattern = value;
                var text = value;
                _rateLimiter.Throttle(
                    TimeSpan.FromMilliseconds(500),
                    () =>
                    {
                        if (string.IsNullOrWhiteSpace(text))
                        {
                            text = _tfsUtility.GetCurrentUser();
                        }

                        Users.Clear();
                        UserShelvesetNames.Clear();

                        var users = _tfsUtility.GetUsers(text);
                        foreach (var user in users.OrderByDescending(
                            x => x.DisplayName.StartsWith(text, StringComparison.OrdinalIgnoreCase) || x.Code.StartsWith(text, StringComparison.OrdinalIgnoreCase)))
                        {
                            Users.Add(user);
                        }

                        User = users.FirstOrDefault();
                    });
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

        private Task WarnBranchExistsAsync([NotNull] string branchName)
        {
            var confirmationViewModel = _confirmationViewModelFactory($"Branch {branchName} already exists. Please choose another name", false);
            _synchronizationContext.Send(
                x =>
                {
                    var confirmationWindow = _confirmationWindowFactory(confirmationViewModel);
                    confirmationWindow.ShowDialog();
                },
                null);
            return confirmationViewModel.UserInput;
        }

        private Task WarnShelvesetDoesNotExistAsync([NotNull] string shelvesetName)
        {
            var confirmationViewModel = _confirmationViewModelFactory($"Shelveset {shelvesetName} does not exist. Please select an existing one", false);
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
                            await WarnShelvesetDoesNotExistAsync(shelvesetName);
                            return;
                        }

                        var branchExists = _gitUtility.BranchExists(_gitInfo, branchName ?? throw new InvalidOperationException());
                        if (branchExists)
                        {
                            await WarnBranchExistsAsync(branchName);
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

        private void OpenShelvesetInBrowser()
        {
            if (ShelvesetName == null || User == null)
            {
                throw new InvalidOperationException("Shelveset or user are not set");
            }

            Process.Start(new ProcessStartInfo(_tfsUtility.GetShelvesetUrl(new ShelvesetData(ShelvesetName, User.Code), _tfsInfo)));
        }

        private void CopyShelvesetToClipboard()
        {
            _cmdUtility.CopyToClipboard(ShelvesetName);
        }
    }
}