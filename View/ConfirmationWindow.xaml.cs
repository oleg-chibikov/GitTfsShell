using System;
using GitTfsShell.ViewModel;
using JetBrains.Annotations;

namespace GitTfsShell.View
{
    internal sealed partial class ConfirmationWindow : IConfirmationWindow
    {
        public ConfirmationWindow([NotNull] ConfirmationViewModel confirmationViewModel)
        {
            InitializeComponent();
            DataContext = confirmationViewModel ?? throw new ArgumentNullException(nameof(confirmationViewModel));
        }
    }
}