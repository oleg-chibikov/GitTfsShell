using GitTfsShell.ViewModel;

namespace GitTfsShell.View
{
    internal sealed partial class MainWindow : IMainWindow
    {
        public MainWindow(MainViewModel mainViewModel)
        {
            InitializeComponent();
            DataContext = mainViewModel;
        }
    }
}