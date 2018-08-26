using System;
using System.Diagnostics;

namespace GitTfsShell.Core
{
    internal static class ProcessCommands
    {
        internal static void ViewLogs()
        {
            Process.Start($@"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}\Scar\{nameof(GitTfsShell)}\Logs\Full.log");
        }
    }
}
