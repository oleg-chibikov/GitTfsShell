using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Easy.MessageHub;
using GitTfsShell.Core;
using GitTfsShell.Data;
using GitTfsShell.Properties;
using JetBrains.Annotations;
using Microsoft.WindowsAPICodePack.Dialogs;
using PropertyChanged;
using Scar.Common.Async;
using Scar.Common.Events;
using Scar.Common.Messages;
using Scar.Common.Processes;
using Scar.Common.WPF.Commands;

namespace GitTfsShell.ViewModel
{
    [AddINotifyPropertyChangedInterface]
    [UsedImplicitly]
    public sealed class MainViewModel : IDisposable
    {
        [NotNull]
        private readonly ICancellationTokenSourceProvider _cancellationTokenSourceProvider;

        [NotNull]
        private readonly ICmdUtility _cmdUtility;

        [NotNull]
        private readonly CommonOpenFileDialog _dialog;

        [NotNull]
        private readonly IGitTfsUtility _gitTfsUtility;

        [NotNull]
        private readonly IGitUtility _gitUtility;

        private readonly object _lockObject = new object();

        [NotNull]
        private readonly IMessageHub _messageHub;

        [NotNull]
        private readonly Func<string, TfsInfo, PullViewModel> _pullViewModelFactory;

        [NotNull]
        private readonly Func<string, GitInfo, TfsInfo, ShelveViewModel> _shelveViewModelFactory;

        [NotNull]
        private readonly IList<Guid> _subscriptionTokens = new List<Guid>();

        [NotNull]
        private readonly SynchronizationContext _synchronizationContext;

        [NotNull]
        private readonly ITfsUtility _tfsUtility;

        [NotNull]
        private readonly Func<string, GitInfo, TfsInfo, UnshelveViewModel> _unshelveViewModelFactory;

        [CanBeNull]
        private GitInfo _gitInfo;

        private bool _isLoading;

        [CanBeNull]
        private TfsInfo _tfsInfo;

        public MainViewModel(
            [NotNull] SynchronizationContext synchronizationContext,
            [NotNull] IProcessUtility processUtility,
            [NotNull] IMessageHub messageHub,
            [NotNull] ICmdUtility cmdUtility,
            [NotNull] IGitUtility gitUtility,
            [NotNull] Func<string, GitInfo, TfsInfo, ShelveViewModel> shelveViewModelFactory,
            [NotNull] Func<string, GitInfo, TfsInfo, UnshelveViewModel> unshelveViewModelFactory,
            [NotNull] Func<string, TfsInfo, PullViewModel> pullViewModelFactory,
            [NotNull] IGitTfsUtility gitTfsUtility,
            [NotNull] ITfsUtility tfsUtility,
            [NotNull] ICancellationTokenSourceProvider cancellationTokenSourceProvider)
        {
            _tfsUtility = tfsUtility;
            _cancellationTokenSourceProvider = cancellationTokenSourceProvider ?? throw new ArgumentNullException(nameof(cancellationTokenSourceProvider));
            _pullViewModelFactory = pullViewModelFactory ?? throw new ArgumentNullException(nameof(pullViewModelFactory));
            _synchronizationContext = synchronizationContext ?? throw new ArgumentNullException(nameof(synchronizationContext));
            processUtility = processUtility ?? throw new ArgumentNullException(nameof(processUtility));
            _messageHub = messageHub ?? throw new ArgumentNullException(nameof(messageHub));
            _cmdUtility = cmdUtility ?? throw new ArgumentNullException(nameof(cmdUtility));
            _gitUtility = gitUtility ?? throw new ArgumentNullException(nameof(gitUtility));
            _gitTfsUtility = gitTfsUtility ?? throw new ArgumentNullException(nameof(gitTfsUtility));
            _shelveViewModelFactory = shelveViewModelFactory ?? throw new ArgumentNullException(nameof(shelveViewModelFactory));
            _unshelveViewModelFactory = unshelveViewModelFactory ?? throw new ArgumentNullException(nameof(unshelveViewModelFactory));

            processUtility.ProcessMessageFired += ProcessUtility_ProcessMessageFired;
            processUtility.ProcessErrorFired += ProcessUtility_ProcessErrorFired;

            ChooseDirectoryCommand = new CorrelationCommand(ChooseDirectory, () => CanBrowse);
            SetDirectoryCommand = new CorrelationCommand<string>(SetDirectoryAsync, directory => CanBrowse);
            PullCommand = new CorrelationCommand(GitTfsPull, () => CanExecuteGitTfsAction);
            OpenShelveDialogCommand = new CorrelationCommand(OpenShelveDialogAsync, () => CanExecuteGitTfsAction);
            OpenUnshelveDialogCommand = new CorrelationCommand(OpenUnshelveDialogAsync, () => CanExecuteGitTfsAction);
            WindowClosingCommand = new AsyncCorrelationCommand(WindowClosing);
            CancelCommand = new CorrelationCommand(Cancel, () => CanCancel);
            ShowLogsCommand = new CorrelationCommand(ProcessCommands.ViewLogs);
            _dialog = new CommonOpenFileDialog
            {
                IsFolderPicker = true,
                InitialDirectory = DirectoryPath
            };
            if (!string.IsNullOrWhiteSpace(Settings.Default.DirectoryPath))
            {
                SetDirectoryAsync(Settings.Default.DirectoryPath);
            }

            _subscriptionTokens.Add(messageHub.Subscribe<Message>(OnNewMessage));
            _subscriptionTokens.Add(messageHub.Subscribe<TaskState>(OnTaskAction));
            _subscriptionTokens.Add(messageHub.Subscribe<DialogType>(OnDialogChanged));
            _subscriptionTokens.Add(messageHub.Subscribe<GitInfo>(OnGitInfoChanged));
            _subscriptionTokens.Add(messageHub.Subscribe<TfsInfo>(OnTfsInfoChanged));
            _subscriptionTokens.Add(messageHub.Subscribe<ShelvesetData>(OnShelvesetEvent));
        }

        public bool CanBrowse => !IsLoading;

        [DependsOn(nameof(IsLoading))]
        public bool CanCancel => IsLoading;

        [NotNull]
        public IRefreshableCommand CancelCommand { get; }

        [NotNull]
        public IRefreshableCommand ChooseDirectoryCommand { get; }

        public string CreatedShelvesetName { get; private set; }

        public string CreatedShelvesetUrl { get; private set; }

        public DialogType DialogType { get; private set; }

        [NotNull]
        public string DirectoryPath { get; private set; } = string.Empty;

        [CanBeNull]
        public GitInfo GitInfo
        {
            get => _gitInfo;
            private set
            {
                _gitInfo = value;
                RaiseGitTfsCommandsCanExecuteChanged();
            }
        }

        [DependsOn(nameof(TfsInfo), nameof(GitInfo))]
        public bool HasInfo => GitInfo != null || TfsInfo != null;

        public bool IsDialogOpen { get; private set; }

        [NotNull]
        public ObservableCollection<Message> Logs { get; } = new ObservableCollection<Message>();

        [NotNull]
        public IRefreshableCommand OpenShelveDialogCommand { get; }

        [NotNull]
        public IRefreshableCommand OpenUnshelveDialogCommand { get; }

        [NotNull]
        public IRefreshableCommand PullCommand { get; }

        [NotNull]
        public ICommand ShowLogsCommand { get; }

        [NotNull]
        public IRefreshableCommand SetDirectoryCommand { get; }

        [CanBeNull]
        public ShelveViewModel ShelveViewModel { get; private set; }

        [CanBeNull]
        public TfsInfo TfsInfo
        {
            get => _tfsInfo;
            private set
            {
                _tfsInfo = value;
                RaiseGitTfsCommandsCanExecuteChanged();
            }
        }

        [CanBeNull]
        public UnshelveViewModel UnshelveViewModel { get; private set; }

        [NotNull]
        public ICommand WindowClosingCommand { get; }

        private bool CanExecuteGitTfsAction => !IsLoading && TfsInfo != null && GitInfo != null;

        [AlsoNotifyFor(nameof(DirectoryPath))]
        private bool DirectoryReRenderSwitch { get; set; }

        private bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                RaiseGitTfsCommandsCanExecuteChanged();
                RaiseBrowseCommandsCanExecuteChanged();
                CancelCommand.RaiseCanExecuteChanged();
            }
        }

        public void Dispose()
        {
            foreach (var subscriptionToken in _subscriptionTokens)
            {
                _messageHub.Unsubscribe(subscriptionToken);
            }

            _subscriptionTokens.Clear();
            ClearAll();
        }

        private static MessageBoxResult ConfirmRepositoryCreation()
        {
            var confirmationResult = MessageBox.Show(
                "No GIT repository detected. Would you like to create one (could take a considerable amount of time)?",
                "Create GIT repository",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            return confirmationResult;
        }

        private void Cancel()
        {
            _cancellationTokenSourceProvider.Cancel();
            _messageHub.Publish("Operation canceled".ToWarning());
        }

        private void ChooseDirectory()
        {
            var result = _dialog.ShowDialog();
            if (result != CommonFileDialogResult.Ok)
            {
                return;
            }

            var directoryPath = _dialog.FileNames.Single();

            SetDirectoryAsync(directoryPath);
        }

        private void ClearAll()
        {
            DirectoryPath = string.Empty;
            _synchronizationContext.Send(x => Logs.Clear(), null);
            GitInfo = null;
            TfsInfo = null;
        }

        private async void GitTfsPull()
        {
            var pullViewModel = _pullViewModelFactory(DirectoryPath, TfsInfo);
            await pullViewModel.PullAsync().ConfigureAwait(false);
        }

        private void OnDialogChanged(DialogType dialogType)
        {
            switch (dialogType)
            {
                case DialogType.None:
                    IsDialogOpen = false;
                    return;
                case DialogType.Shelve:
                    IsDialogOpen = true;
                    break;
                case DialogType.Unshelve:
                    IsDialogOpen = true;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(dialogType), dialogType, null);
            }

            DialogType = dialogType;
        }

        private void OnGitInfoChanged([CanBeNull] GitInfo gitInfo)
        {
            GitInfo?.Repo.Dispose();
            GitInfo = gitInfo;
        }

        private void OnNewMessage([NotNull] Message message)
        {
            if (message.Exception == null)
            {
                _synchronizationContext.Send(x => Logs.Add(message), null);
            }
        }

        private void OnShelvesetEvent([NotNull] ShelvesetData data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            CreatedShelvesetName = data.Name;
            CreatedShelvesetUrl = Settings.Default.TfsUri + "_versionControl/shelveset?ss=" + _tfsUtility.GetShelvesetQualifiedName(data.User, data.Name);
        }

        private void OnTaskAction(TaskState taskState)
        {
            switch (taskState)
            {
                case TaskState.Started:
                    _synchronizationContext.Send(
                        x =>
                        {
                            IsLoading = true;
                            Logs.Clear();
                        },
                        null);
                    break;
                case TaskState.Error:
                case TaskState.Finished:
                    _synchronizationContext.Send(x => IsLoading = false, null);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(taskState));
            }
        }

        private void OnTfsInfoChanged([CanBeNull] TfsInfo tfsInfo)
        {
            TfsInfo = tfsInfo;
        }

        private async void OpenShelveDialogAsync()
        {
            var gitInfoTask = _gitUtility.GetInfoAsync(DirectoryPath);
            var tfsInfoTask = _tfsUtility.GetInfoAsync(DirectoryPath);
            await Task.WhenAll(gitInfoTask, tfsInfoTask).ConfigureAwait(false);
            var gitInfo = await gitInfoTask.ConfigureAwait(false);
            var tfsInfo = await tfsInfoTask.ConfigureAwait(false);
            _messageHub.Publish(gitInfo);
            _messageHub.Publish(tfsInfo);
            ShelveViewModel = _shelveViewModelFactory(DirectoryPath, gitInfo, tfsInfo);
        }

        private async void OpenUnshelveDialogAsync()
        {
            var gitInfoTask = _gitUtility.GetInfoAsync(DirectoryPath);
            var tfsInfoTask = _tfsUtility.GetInfoAsync(DirectoryPath);
            await Task.WhenAll(gitInfoTask, tfsInfoTask).ConfigureAwait(false);
            var gitInfo = await gitInfoTask.ConfigureAwait(false);
            var tfsInfo = await tfsInfoTask.ConfigureAwait(false);
            _messageHub.Publish(gitInfo);
            _messageHub.Publish(tfsInfo);
            UnshelveViewModel = _unshelveViewModelFactory(DirectoryPath, gitInfo, tfsInfo);
        }

        private void ProcessUtility_ProcessErrorFired(object sender, [NotNull] EventArgs<string> e)
        {
            _messageHub.Publish(e.Parameter.ToError());
        }

        private void ProcessUtility_ProcessMessageFired(object sender, [NotNull] EventArgs<string> e)
        {
            var type = MessageType.Message;
            if (e.Parameter.IndexOf("fatal", StringComparison.OrdinalIgnoreCase) != -1 || e.Parameter.IndexOf("error", StringComparison.OrdinalIgnoreCase) != -1)
            {
                type = MessageType.Error;
            }
            else if (e.Parameter.IndexOf("warning", StringComparison.OrdinalIgnoreCase) != -1)
            {
                type = MessageType.Warning;
            }

            _messageHub.Publish(new Message(e.Parameter, type));
        }

        private void RaiseBrowseCommandsCanExecuteChanged()
        {
            ChooseDirectoryCommand.RaiseCanExecuteChanged();
            SetDirectoryCommand.RaiseCanExecuteChanged();
        }

        private void RaiseGitTfsCommandsCanExecuteChanged()
        {
            lock (_lockObject)
            {
                PullCommand.RaiseCanExecuteChanged();
                OpenShelveDialogCommand.RaiseCanExecuteChanged();
                OpenUnshelveDialogCommand.RaiseCanExecuteChanged();
            }
        }

        private async void SetDirectoryAsync(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                MessageBox.Show("Please enter the directory");
                return;
            }

            TfsInfo tfsInfo = null;
            GitInfo gitInfo = null;
            ClearAll();
            await _cmdUtility.ExecuteTaskAsync(
                    async cancellationToken =>
                    {
                        directoryPath = directoryPath.TrimEnd(Path.DirectorySeparatorChar);
                        var tfsInfoTask = _tfsUtility.GetInfoAsync(directoryPath);
                        var gitInfoTask = _gitUtility.GetInfoAsync(directoryPath);
                        tfsInfo = await tfsInfoTask.ConfigureAwait(false);
                        if (tfsInfo == null)
                        {
                            _messageHub.Publish("Not a TFS workspace".ToError());
                            DirectoryReRenderSwitch = !DirectoryReRenderSwitch;
                            return;
                        }

                        gitInfo = await gitInfoTask.ConfigureAwait(false);

                        if (gitInfo == null)
                        {
                            var confirmationResult = ConfirmRepositoryCreation();
                            if (confirmationResult == MessageBoxResult.Yes)
                            {
                                _tfsUtility.GetLatest(tfsInfo);
                                await _gitTfsUtility.CloneAsync(tfsInfo, directoryPath, cancellationToken).ConfigureAwait(false);
                                gitInfo = await _gitUtility.GetInfoAsync(directoryPath).ConfigureAwait(false);
                            }
                            else
                            {
                                _messageHub.Publish("Not a GIT repository".ToError());
                                DirectoryReRenderSwitch = !DirectoryReRenderSwitch;
                                return;
                            }
                        }

                        Settings.Default.DirectoryPath = directoryPath;
                        Settings.Default.Save();
                    },
                    false)
                .ConfigureAwait(false);

            _synchronizationContext.Send(
                x =>
                {
                    TfsInfo = tfsInfo;
                    GitInfo = gitInfo;
                    DirectoryPath = directoryPath;
                    RaiseBrowseCommandsCanExecuteChanged();
                    RaiseGitTfsCommandsCanExecuteChanged();
                },
                null);
        }

        private async Task WindowClosing()
        {
            var notCompleted = !_cancellationTokenSourceProvider.CurrentTask.IsCompleted;
            _cancellationTokenSourceProvider.Cancel();
            if (notCompleted)
            {
                _messageHub.Publish("Operation canceled".ToWarning());
            }

            await _cancellationTokenSourceProvider.CurrentTask.ConfigureAwait(false);
        }
    }
}