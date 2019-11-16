﻿using ModMyFactory.BaseTypes;
using ModMyFactory.Game;
using ModMyFactory.Mods;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ModMyFactory
{
    /// <summary>
    /// Manages Factorio instances and their mods.
    /// </summary>
    public sealed class Manager : IEnumerable<ManagedFactorioInstance>
    {
        readonly List<ManagedFactorioInstance> _managedInstances;
        readonly Dictionary<AccurateVersion, ModManager> _modManagers;

        /// <summary>
        /// The list of instances being managed.
        /// </summary>
        public IReadOnlyList<ManagedFactorioInstance> ManagedInstances { get; }

        public Manager()
        {
            _managedInstances = new List<ManagedFactorioInstance>();
            ManagedInstances = new ReadOnlyCollection<ManagedFactorioInstance>(_managedInstances);
            _modManagers = new Dictionary<AccurateVersion, ModManager>();
        }

        ModManager GetModManager(AccurateVersion factorioVersion)
        {
            if (!_modManagers.TryGetValue(factorioVersion, out var result))
            {
                result = new ModManager(factorioVersion);
                _modManagers.Add(factorioVersion, result);
            }
            return result;
        }

        /// <summary>
        /// Adds a Factorio instance to the manager.
        /// </summary>
        /// <param name="instance">The instance to add.</param>
        public void AddInstance(ManagedFactorioInstance instance)
        {
            instance.HasManagerAttached = true;
            var modManager = GetModManager(instance.Version.ToMajor());
            
            _managedInstances.Add(instance);
        }

        /// <summary>
        /// Adds a Factorio instance to the manager.
        /// </summary>
        /// <param name="instance">The instance to add.</param>
        public ManagedFactorioInstance AddInstance(IFactorioInstance instance)
        {
            var managedInstance = instance.ToManaged();
            AddInstance(managedInstance);
            return managedInstance;
        }

        /// <summary>
        /// Removes a Factorio instance from the manager.
        /// </summary>
        /// <param name="instance">The instance to remove.</param>
        public void RemoveInstance(ManagedFactorioInstance instance)
        {
            _managedInstances.Remove(instance);
            instance.UnlinkModDirectoryInternal();
            instance.HasManagerAttached = false;
        }

        public IEnumerator<ManagedFactorioInstance> GetEnumerator() => _managedInstances.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
