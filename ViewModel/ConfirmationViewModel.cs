using System;
using System.Threading.Tasks;
using JetBrains.Annotations;
using PropertyChanged;
using Scar.Common.MVVM.Commands;
using Scar.Common.MVVM.ViewModel;

namespace GitTfsShell.ViewModel
{
    [AddINotifyPropertyChangedInterface]
    [UsedImplicitly]
    public sealed class ConfirmationViewModel : BaseViewModel
    {
        private readonly TaskCompletionSource<bool> _taskCompletionSource;

        public ConfirmationViewModel([NotNull] ICommandManager commandManager, bool showButtons, [NotNull] string text)
            : base(commandManager)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
            DeclineCommand = AddCommand(Decline);
            ConfirmCommand = AddCommand(Confirm);
            WindowClosedCommand = AddCommand(Decline);
            _taskCompletionSource = new TaskCompletionSource<bool>();
            ShowButtons = showButtons;
        }

        [NotNull]
        public string Text { get; }

        public bool ShowButtons { get; }

        [DoNotNotify]
        public Task<bool> UserInput => _taskCompletionSource.Task;

        [NotNull]
        public IRefreshableCommand DeclineCommand { get; }

        [NotNull]
        public IRefreshableCommand ConfirmCommand { get; }

        [NotNull]
        public IRefreshableCommand WindowClosedCommand { get; }

        private void Decline()
        {
            if (_taskCompletionSource.Task.IsCompleted)
            {
                return;
            }

            _taskCompletionSource.SetResult(ShowButtons == false);
            CloseWindow();
        }

        private void Confirm()
        {
            if (_taskCompletionSource.Task.IsCompleted)
            {
                return;
            }

            _taskCompletionSource.SetResult(true);
            CloseWindow();
        }
    }
}