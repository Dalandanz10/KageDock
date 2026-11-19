
using System;
using System.Collections.Generic;
using System.Linq;
using KageDock.Contracts;

namespace KageDock.Content
{
    public sealed class DockContentHost : IDisposable
    {
        private readonly Dictionary<string, DockModuleDescriptor> _descriptors =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, IDockContentModule> _modules =
            new(StringComparer.OrdinalIgnoreCase);

        private bool _disposed;

        public IDockContentModule? ActiveModule { get; private set; }

        public IReadOnlyCollection<DockModuleDescriptor> RegisteredModules =>
            _descriptors.Values.OrderBy(x => x.Order).ToArray();

        public void Register(DockModuleDescriptor descriptor)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(descriptor);

            if (_descriptors.ContainsKey(descriptor.Id))
            {
                throw new InvalidOperationException(
                    $"A module with ID '{descriptor.Id}' is already registered.");
            }

            _descriptors.Add(descriptor.Id, descriptor);
        }


        public IDockContentModule Activate(string id)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (!_descriptors.TryGetValue(id, out var descriptor))
            {
                throw new KeyNotFoundException(
                    $"No module is registered with ID '{id}'.");
            }

            if (ActiveModule?.Id.Equals(
                id, StringComparison.OrdinalIgnoreCase) == true)
            {
                return ActiveModule;
            }

            var nextModule = GetOrCreateModule(descriptor);
            var previousModule = ActiveModule;

            previousModule?.OnDeactivated();

            try
            {
                nextModule.OnActivated();
                ActiveModule = nextModule;
                return nextModule;
            }
            catch
            {
                if (previousModule != null)
                {
                    try
                    {
                        previousModule.OnActivated();
                    }
                    catch
                    {
                        ActiveModule = null;
                        throw;
                    }
                }

                ActiveModule = previousModule;
                throw;
            }
        }

        public void DeactivateActive()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (ActiveModule == null)
                return;

            ActiveModule.OnDeactivated();
            ActiveModule = null;
        }

        public IDockContentModule Prepare(string id)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (!_descriptors.TryGetValue(id, out var descriptor))
                throw new KeyNotFoundException(
                    $"No module is registered with ID '{id}'.");

            return GetOrCreateModule(descriptor);
        }

        private IDockContentModule GetOrCreateModule(
            DockModuleDescriptor descriptor)
        {
            if (_modules.TryGetValue(descriptor.Id, out var existing))
                return existing;

            var module = descriptor.Factory();

            if (module == null)
            {
                throw new InvalidOperationException(
                    $"Factory for module '{descriptor.Id}' returned null.");
            }

            if (!module.Id.Equals(
                descriptor.Id, StringComparison.OrdinalIgnoreCase))
            {
                module.Dispose();

                throw new InvalidOperationException(
                    $"Module factory ID mismatch. Expected '{descriptor.Id}', " +
                    $"but received '{module.Id}'.");
            }

            _modules.Add(descriptor.Id, module);

            return module;
        }


        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            List<Exception> errors = new();

            try
            {
                ActiveModule?.OnDeactivated();
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
            finally
            {
                ActiveModule = null;
            }

            foreach (var module in _modules.Values)
            {
                try
                {
                    module.Dispose();
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            }

            _modules.Clear();
            _descriptors.Clear();

            if (errors.Count > 0)
            {
                throw new AggregateException(
                    "One or more content modules failed to shut down.",
                    errors);
            }
        }
    }
}
