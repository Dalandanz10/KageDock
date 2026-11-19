
using System;

namespace KageDock.Contracts
{
    public sealed class DockModuleDescriptor
    {
        public string Id { get; }
        public string DisplayName { get; }
        public string? IconKey { get; }
        public int Order { get; }

        public Func<IDockContentModule> Factory { get; }

        public DockModuleDescriptor(
            string id,
            string displayName,
            Func<IDockContentModule> factory,
            int order = 0,
            string? iconKey = null)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException(
                    "Module ID cannot be empty.", nameof(id));

            if (string.IsNullOrWhiteSpace(displayName))
                throw new ArgumentException(
                    "Display name cannot be empty.", nameof(displayName));

            ArgumentNullException.ThrowIfNull(factory);

            Id = id;
            DisplayName = displayName;
            Factory = factory;
            Order = order;
            IconKey = iconKey;
        }
    }
}
