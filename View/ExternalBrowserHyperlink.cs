using System.Diagnostics;
using System.Windows.Documents;
using System.Windows.Navigation;
using JetBrains.Annotations;

namespace GitTfsShell.View
{
    // TODO: Wrap in a SelectableTextblock and Move to Lib
    /// <summary>
    /// Opens <see cref="Hyperlink.NavigateUri" /> in a default system browser
    /// </summary>
    public sealed class ExternalBrowserHyperlink : Hyperlink
    {
        public ExternalBrowserHyperlink()
        {
            RequestNavigate += OnRequestNavigate;
        }

        private static void OnRequestNavigate(object sender, [NotNull] RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri));
            e.Handled = true;
        }
    }
}