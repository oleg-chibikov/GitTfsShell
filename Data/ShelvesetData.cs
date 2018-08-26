using System;
using JetBrains.Annotations;

namespace GitTfsShell.Data
{
    public class ShelvesetData
    {
        public ShelvesetData([NotNull] string name)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }

        public ShelvesetData([NotNull] string name, [NotNull] string user)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            User = user ?? throw new ArgumentNullException(nameof(user));
        }

        [NotNull]
        public string Name { get; }

        [CanBeNull]
        public string User { get; }
    }
}