using System;
using System.Threading.Tasks;
using Easy.MessageHub;
using GitTfsShell.Core;
using GitTfsShell.Data;
using JetBrains.Annotations;
using PropertyChanged;

namespace GitTfsShell.ViewModel
{
    [AddINotifyPropertyChangedInterface]
    [UsedImplicitly]
    public sealed class PullViewModel
    {
        [NotNull]
        private readonly ICmdUtility _cmdUtility;

        [NotNull]
        private readonly string _directoryPath;

        [NotNull]
        private readonly IGitTfsUtility _gitTfsUtility;

        [NotNull]
        private readonly IGitUtility _gitUtility;

        [NotNull]
        private readonly IMessageHub _messageHub;

        [NotNull]
        private readonly TfsInfo _tfsInfo;

        [NotNull]
        private readonly ITfsUtility _tfsUtility;

        public PullViewModel(
            [NotNull] string directoryPath,
            [NotNull] IMessageHub messageHub,
            [NotNull] IGitTfsUtility gitTfsUtility,
            [NotNull] ITfsUtility tfsUtility,
            [NotNull] IGitUtility gitUtility,
            [NotNull] TfsInfo tfsInfo,
            [NotNull] ICmdUtility cmdUtility)
        {
            _messageHub = messageHub ?? throw new ArgumentNullException(nameof(messageHub));
            _gitTfsUtility = gitTfsUtility ?? throw new ArgumentNullException(nameof(gitTfsUtility));
            _tfsUtility = tfsUtility ?? throw new ArgumentNullException(nameof(tfsUtility));
            _gitUtility = gitUtility ?? throw new ArgumentNullException(nameof(gitUtility));
            _tfsInfo = tfsInfo ?? throw new ArgumentNullException(nameof(tfsInfo));
            _cmdUtility = cmdUtility ?? throw new ArgumentNullException(nameof(cmdUtility));

            _directoryPath = directoryPath ?? throw new ArgumentNullException(nameof(directoryPath));
        }

        internal async Task PullAsync()
        {
            await _cmdUtility.ExecuteTaskAsync(
                    async cancellationToken =>
                    {
                        await _gitTfsUtility.PullAsync(_tfsInfo, _directoryPath, cancellationToken).ConfigureAwait(false);
                        _tfsUtility.GetLatest(_tfsInfo);
                        _messageHub.Publish(await _gitUtility.GetInfoAsync(_directoryPath).ConfigureAwait(false));
                    })
                .ConfigureAwait(false);
        }
    }
}