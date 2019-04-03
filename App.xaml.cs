using System;
using System.Threading.Tasks;
using System.Windows;
using Autofac;
using GitTfsShell.Core;
using GitTfsShell.Properties;
using GitTfsShell.View;
using GitTfsShell.ViewModel;
using JetBrains.Annotations;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.Framework.Client;
using Microsoft.TeamFoundation.VersionControl.Client;
using Scar.Common;
using Scar.Common.ApplicationStartup;
using Scar.Common.Async;
using Scar.Common.Messages;
using Scar.Common.MVVM.Commands;
using Scar.Common.Processes;

namespace GitTfsShell
{
    internal sealed partial class App
    {
        [NotNull]
        private readonly Func<string, bool, ConfirmationViewModel> _confirmationViewModelFactory;

        [NotNull]
        private readonly Func<ConfirmationViewModel, IConfirmationWindow> _confirmationWindowFactory;

        [NotNull]
        private readonly TfsTeamProjectCollection _tfs;

        public App()
        {
            _tfs = new TfsTeamProjectCollection(new Uri(Settings.Default.TfsUri));
            _confirmationViewModelFactory = Container.Resolve<Func<string, bool, ConfirmationViewModel>>();
            _confirmationWindowFactory = Container.Resolve<Func<ConfirmationViewModel, IConfirmationWindow>>();
        }

        protected override NewInstanceHandling NewInstanceHandling => NewInstanceHandling.AllowMultiple;

        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);
            _tfs.Dispose();
        }

        protected override void OnStartup()
        {
            Container.Resolve<IMainWindow>().Restore();
        }

        protected override void RegisterDependencies(ContainerBuilder builder)
        {
            builder.RegisterType<ApplicationCommandManager>().AsImplementedInterfaces().SingleInstance();
            builder.RegisterType<ProcessUtility>().AsImplementedInterfaces().SingleInstance();
            builder.RegisterType<GitUtility>().AsImplementedInterfaces().SingleInstance();
            builder.RegisterType<TfsUtility>().AsImplementedInterfaces().SingleInstance();
            builder.RegisterType<CmdUtility>().AsImplementedInterfaces().SingleInstance();
            builder.RegisterType<GitTfsUtility>().AsImplementedInterfaces().SingleInstance();
            builder.RegisterType<CancellationTokenSourceProvider>().AsImplementedInterfaces().SingleInstance();
            builder.RegisterType<RateLimiter>().AsImplementedInterfaces().InstancePerDependency();
            builder.RegisterType<MainWindow>().AsImplementedInterfaces().InstancePerDependency();
            builder.RegisterType<ConfirmationWindow>().AsImplementedInterfaces().InstancePerDependency();
            builder.RegisterType<MainViewModel>().AsSelf().InstancePerDependency();
            builder.RegisterType<ShelveViewModel>().AsSelf().InstancePerDependency();
            builder.RegisterType<UnshelveViewModel>().AsSelf().InstancePerDependency();
            builder.RegisterType<PullViewModel>().AsSelf().InstancePerDependency();
            builder.RegisterType<ConfirmationViewModel>().AsSelf().InstancePerDependency();
            builder.Register(c => _tfs.GetService<VersionControlServer>()).AsSelf().SingleInstance();
            builder.Register(c => _tfs.GetService<IIdentityManagementService>()).AsImplementedInterfaces().SingleInstance();
        }

        protected override void ShowMessage(Message message)
        {
            switch (message.Type)
            {
                case MessageType.Success:
                    Logger.Info(message.Text);
                    break;
                case MessageType.Message:
                    Logger.Debug(message.Text);
                    break;
            }

            if (message.Exception != null)
            {
                ShowPopup(message).Wait();
            }
        }

        private Task ShowPopup([NotNull] Message message)
        {
            if (SynchronizationContext == null)
            {
                MessageBox.Show(message.Text, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return Task.CompletedTask;
            }

            var confirmationViewModel = _confirmationViewModelFactory(message.Text, false);
            SynchronizationContext.Send(
                x =>
                {
                    var confirmationWindow = _confirmationWindowFactory(confirmationViewModel);
                    confirmationWindow.ShowDialog();
                },
                null);
            return confirmationViewModel.UserInput;
        }
    }
}