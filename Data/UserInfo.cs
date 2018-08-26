using System;
using JetBrains.Annotations;

namespace GitTfsShell.Data
{
    public sealed class UserInfo
    {
        public UserInfo([NotNull] string name, [NotNull] string code)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Code = code ?? throw new ArgumentNullException(nameof(code));
        }

        [NotNull]
        public string Code { get; }

        [NotNull]
        public string DisplayName => Name + $" ({Code})";

        [NotNull]
        public string Name { get; }
    }
}