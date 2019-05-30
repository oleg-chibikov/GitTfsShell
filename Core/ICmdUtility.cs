using System;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace GitTfsShell.Core
{
    public interface ICmdUtility
    {
        [NotNull]
        Task ExecuteCommandAsync([NotNull] string executable, [NotNull] string command, [CanBeNull] string directoryPath, CancellationToken cancellationToken);

        [NotNull]
        Task ExecuteTaskAsync([NotNull] Func<CancellationToken, Task> action, bool notifySuccess = true, bool preventCancellation = false);

        void CopyToClipboard([CanBeNull] string text);
    }
}