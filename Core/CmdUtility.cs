using System;
using System.Threading;
using System.Threading.Tasks;
using Easy.MessageHub;
using GitTfsShell.Data;
using JetBrains.Annotations;
using Scar.Common.Async;
using Scar.Common.Messages;
using Scar.Common.Processes;

namespace GitTfsShell.Core
{
    [UsedImplicitly]
    internal sealed class CmdUtility : ICmdUtility
    {
        [NotNull]
        private readonly ICancellationTokenSourceProvider _cancellationTokenSourceProvider;

        [NotNull]
        private readonly IMessageHub _messageHub;

        [NotNull]
        private readonly IProcessUtility _processUtility;

        public CmdUtility([NotNull] IProcessUtility processUtility, [NotNull] IMessageHub messageHub, [NotNull] ICancellationTokenSourceProvider cancellationTokenSourceProvider)
        {
            _processUtility = processUtility ?? throw new ArgumentNullException(nameof(processUtility));
            _messageHub = messageHub ?? throw new ArgumentNullException(nameof(messageHub));
            _cancellationTokenSourceProvider = cancellationTokenSourceProvider;
        }

        public async Task ExecuteCommandAsync(string executable, string command, string directoryPath, CancellationToken cancellationToken)
        {
            if (executable == null)
            {
                throw new ArgumentNullException(nameof(executable));
            }

            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            _messageHub.Publish($"Executing '{executable}' '{command}'...".ToMessage());
            var result = await _processUtility.ExecuteCommandAsync(executable, command, cancellationToken, workingDirectory: directoryPath).ConfigureAwait(false);
            if (result.IsError)
            {
                throw new InvalidOperationException($"Cannot execute '{command}'");
            }

            _messageHub.Publish($"'{command}' has been executed successfully".ToSuccess());
        }

        public async Task ExecuteTaskAsync(Func<CancellationToken, Task> action, bool notifySuccess)
        {
            await _cancellationTokenSourceProvider.ExecuteAsyncOperation(
                    async cancellationToken => await Task.Run(
                            async () =>
                            {
                                _messageHub.Publish(TaskState.Started);
                                try
                                {
                                    await action(cancellationToken).ConfigureAwait(false);
                                    if (notifySuccess)
                                    {
                                        _messageHub.Publish("Task has been executed successfully!".ToSuccess());
                                    }

                                    _messageHub.Publish(TaskState.Finished);
                                }
                                catch (Exception ex)
                                {
                                    _messageHub.Publish(ex.ToMessage());
                                    _messageHub.Publish(TaskState.Error);
                                }
                            },
                            cancellationToken)
                        .ConfigureAwait(false))
                .ConfigureAwait(false);
        }
    }
}