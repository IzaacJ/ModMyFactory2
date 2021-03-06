//  Copyright (C) 2020 Mathis Rech
//
//  This program is free software: you can redistribute it and/or modify
//  it under the terms of the GNU General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.

using ModMyFactory;
using ModMyFactory.BaseTypes;
using ModMyFactory.Mods;
using ModMyFactory.WebApi;
using ModMyFactory.WebApi.Mods;
using ModMyFactoryGUI.Helpers;
using ModMyFactoryGUI.Tasks.Web;
using ModMyFactoryGUI.Views;
using ReactiveUI;
using Serilog;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace ModMyFactoryGUI.ViewModels
{
    internal sealed class OnlineModsViewModel : MainViewModelBase<OnlineModsView>
    {
        private readonly Manager _manager;
        private readonly DownloadQueue _downloadQueue;
        private readonly AutoResetEvent _refreshEvent;
        private ICollection<OnlineModViewModel>? _onlineMods;
        private ModComparer _selectedComparer;
        private AccurateVersion _selectedFactorioVersion;
        private OnlineModViewModel? _selectedMod;
        private string _filter;
        private volatile bool _refreshing = false;

        public bool ModsLoaded { get; private set; }

        public bool LoadingErrorOccurred { get; private set; }

        public string ErrorMessageKey { get; private set; }

        public CollectionView<OnlineModViewModel>? OnlineMods { get; private set; }

        public ICommand RefreshCommand { get; }

        public IList<ModComparer> Comparers { get; }

        public ModComparer SelectedComparer
        {
            get => _selectedComparer;
            set
            {
                if (value != _selectedComparer)
                {
                    _selectedComparer = value;
                    this.RaisePropertyChanged(nameof(SelectedComparer));

                    if (!(OnlineMods is null))
                    {
                        OnlineMods.Comparer = _selectedComparer;
                        this.RaisePropertyChanged(nameof(OnlineMods));
                    }
                }
            }
        }

        public CollectionView<AccurateVersion>? FactorioVersions { get; private set; }

        public AccurateVersion SelectedFactorioVersion
        {
            get => _selectedFactorioVersion;
            set
            {
                var newVal = value.ToFactorioMajor();
                if (newVal != _selectedFactorioVersion)
                {
                    _selectedFactorioVersion = newVal;
                    this.RaisePropertyChanged(nameof(SelectedFactorioVersion));

                    if (!(OnlineMods is null))
                    {
                        OnlineMods.Refresh();
                        this.RaisePropertyChanged(nameof(OnlineMods));
                    }
                }
            }
        }

        public OnlineModViewModel? SelectedMod
        {
            get => _selectedMod;
            set
            {
                if (value != _selectedMod)
                {
                    _selectedMod = value;
                    this.RaisePropertyChanged(nameof(SelectedMod));
                }
            }
        }

        public string Filter
        {
            get => _filter;
            set
            {
                if (value != _filter)
                {
                    _filter = value;
                    this.RaisePropertyChanged(nameof(Filter));

                    if (!(_onlineMods is null))
                    {
                        foreach (var mod in _onlineMods)
                            mod.ApplyFuzzyFilter(_filter);
                    }

                    if (!(OnlineMods is null))
                    {
                        OnlineMods.Refresh();
                        this.RaisePropertyChanged(nameof(OnlineMods));
                    }
                }
            }
        }

        public OnlineModsViewModel(int tabIndex, Manager manager, DownloadQueue downloadQueue)
            : base(tabIndex)
        {
            _manager = manager;
            _downloadQueue = downloadQueue;
            _refreshEvent = new AutoResetEvent(false);
            _filter = string.Empty;
            ErrorMessageKey = string.Empty;
            RefreshCommand = ReactiveCommand.CreateFromTask(RefreshOnlineModsAsync);

            App.Current.Locales.UICultureChanged += (sender, e) =>
            {
                // This is overly complicated but required to force the UI to refresh
                var tmpVs = FactorioVersions;
                var tmpV = SelectedFactorioVersion;

                FactorioVersions = null;
                this.RaisePropertyChanged(nameof(FactorioVersions));
                FactorioVersions = tmpVs;
                this.RaisePropertyChanged(nameof(FactorioVersions));

                _selectedFactorioVersion = tmpV;
                this.RaisePropertyChanged(nameof(SelectedFactorioVersion));
            };

            Comparers = new ModComparer[]
            {
                AlphabeticalModComparer.Instance,
                DownloadCountModComparer.Instance
            };
            _selectedComparer = Comparers[0];

            FactorioVersions = new CollectionView<AccurateVersion>(new AccurateVersion[1] { default });
            SelectedFactorioVersion = default;

            // This is fire-and-forget intentionally
            async void Refresh() => await RefreshOnlineModsAsync();
            Refresh();

            PropertyChanged += async (s, e) => await OnPropertyChanged(s, e);
        }

        public async ValueTask BrowseModFamilyAsync(ModFamily family)
        {
            Filter = string.Empty;

            if (!ModsLoaded)
            {
                if (_refreshing)
                {
                    await _refreshEvent.WaitOneAsync();
                }
                else
                {
                    await RefreshOnlineModsAsync();
                }
                
            }

            if (!LoadingErrorOccurred)
            {
                var mod = _onlineMods!.FirstOrDefault(vm => vm.Info.Name == family.FamilyName);
                if (mod is null)
                {
                    // ToDo: show message
                }
                else
                {
                    SelectedMod = mod;
                    AttachedView!.ScrollModIntoView(mod);
                }
            }
        }

        private async Task OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if ((e.PropertyName == nameof(SelectedMod)) && !(SelectedMod is null))
                await SelectedMod.LoadExtendedInfoAsync();
        }

        private async Task<ICollection<OnlineModViewModel>> LoadOnlineModsAsync()
        {
            var page = await ModApi.RequestPageAsync();
            var result = new List<OnlineModViewModel>(page.Mods.Length);

            foreach (var info in page.Mods)
                result.Add(new OnlineModViewModel(info, _manager, _downloadQueue));
            return result;
        }

        private void DisposeOnlineMods()
        {
            if (!(_onlineMods is null))
            {
                foreach (var onlineMod in _onlineMods)
                    onlineMod.Dispose();
            }
        }

        private bool FilterMod(OnlineModViewModel mod)
        {
            // Filter out all the removed mods
            if (string.IsNullOrWhiteSpace(mod.DisplayName)) return false;
            if (mod.DisplayName.Length == 1) return false;
            if (!mod.FactorioVersion.HasValue) return false;

            // Filter for selected Factorio version
            if ((SelectedFactorioVersion != default) && (mod.FactorioVersion.Value.ToFactorioMajor() != SelectedFactorioVersion)) return false;

            // Filter based on fuzzy search
            return mod.MatchesSearch;
        }

        private void RefreshFactorioVersions()
        {
            SelectedFactorioVersion = default;

            var factorioVersions = new HashSet<AccurateVersion> { default };
            foreach (var mod in _onlineMods!)
            {
                if (mod.FactorioVersion.HasValue)
                    factorioVersions.Add(mod.FactorioVersion.Value.ToFactorioMajor());
            }

            // Required since someone apparently thought it was a good idea to add a mod with Factorio version 0.99
            static bool Filter(AccurateVersion v) => v != (0, 99);

            var prev = FactorioVersions;
            FactorioVersions = new CollectionView<AccurateVersion>(factorioVersions, Comparer<AccurateVersion>.Default, Filter);
            prev?.Dispose();

            this.RaisePropertyChanged(nameof(FactorioVersions));
            this.RaisePropertyChanged(nameof(SelectedFactorioVersion));
        }

        private async Task RefreshOnlineModsAsync()
        {
            if (_refreshing) return;

            _refreshEvent.Reset();
            _refreshing = true;
            ModsLoaded = false;
            this.RaisePropertyChanged(nameof(ModsLoaded));
            LoadingErrorOccurred = false;
            this.RaisePropertyChanged(nameof(LoadingErrorOccurred));
            SelectedMod = null;

            // Avoid memory leak
            DisposeOnlineMods();

            try
            {
                _onlineMods = await LoadOnlineModsAsync();

                foreach (var mod in _onlineMods)
                    mod.ApplyFuzzyFilter(_filter);
            }
            catch (ApiException ex)
            {
                if (ex is ConnectFailureException)
                {
                    // Connection error
                    LoadingErrorOccurred = true;
                    this.RaisePropertyChanged(nameof(LoadingErrorOccurred));
                    ErrorMessageKey = "ConnectionError_Message";
                    this.RaisePropertyChanged(nameof(ErrorMessageKey));
                    Log.Error(ex, "Failed to connect to server");
                }
                else if (ex is TimeoutException)
                {
                    // Timeout
                    LoadingErrorOccurred = true;
                    this.RaisePropertyChanged(nameof(LoadingErrorOccurred));
                    ErrorMessageKey = "TimeoutError_Message";
                    this.RaisePropertyChanged(nameof(ErrorMessageKey));
                    Log.Error(ex, "Timeout while trying to connect to server");
                }
                else
                {
                    // Server error
                    LoadingErrorOccurred = true;
                    this.RaisePropertyChanged(nameof(LoadingErrorOccurred));
                    ErrorMessageKey = "ServerError_Message";
                    this.RaisePropertyChanged(nameof(ErrorMessageKey));
                    Log.Error(ex, "An unknown server error occurred");
                }

                _onlineMods = new List<OnlineModViewModel>(0);
            }

            OnlineMods = new CollectionView<OnlineModViewModel>(_onlineMods, SelectedComparer, FilterMod);
            this.RaisePropertyChanged(nameof(OnlineMods));

            RefreshFactorioVersions();

            ModsLoaded = true;
            this.RaisePropertyChanged(nameof(ModsLoaded));

            _refreshing = false;
            _refreshEvent.Set();
        }
    }
}
