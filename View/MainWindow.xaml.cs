using System;
using GitTfsShell.ViewModel;
using JetBrains.Annotations;

namespace GitTfsShell.View
{
    internal sealed partial class MainWindow : IMainWindow
    {
        public MainWindow([NotNull] MainViewModel mainViewModel)
        {
            InitializeComponent();
            DataContext = mainViewModel ?? throw new ArgumentNullException(nameof(mainViewModel));
        }
    }
}