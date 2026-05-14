using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class PackagesViewModel(
    AuthSessionService authSessionService,
    MobileBootstrapStore bootstrapStore) : ViewModelBase, ISessionStateResettable
{
    public ObservableCollection<PackageListItemViewModel> Packages { get; } = [];
    public bool ShowInitialLoading => IsBusy && Packages.Count == 0;
    public bool ShowEmptyState => HasLoaded && !IsBusy && Packages.Count == 0;

    public void ResetForNewSession()
    {
        ResetLoadState();
        Packages.Clear();
    }

    [RelayCommand]
    private Task LoadPackagesAsync()
    {
        return LoadAsync(async () =>
        {
            await LoadPackagesLocalFirstAsync();
        });
    }

    private async Task LoadPackagesLocalFirstAsync()
    {
        var token = await authSessionService.GetTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            authSessionService.Clear();
            await Shell.Current.GoToAsync("//login");
            return;
        }

        var cached = await bootstrapStore.GetCachedAsync();
        if (cached is not null)
        {
            ApplyPackages(cached.Value.Packages);
            StatusMessage = OfflineCacheService.FormatSavedAt(cached.SavedAt);
        }

        try
        {
            var bootstrap = await bootstrapStore.RefreshAsync(token);
            if (bootstrap is null)
            {
                authSessionService.Clear();
                await Shell.Current.GoToAsync("//login");
                return;
            }

            ApplyPackages(bootstrap.Packages);
            StatusMessage = null;
        }
        catch
        {
            if (cached is null)
            {
                throw;
            }

            StatusMessage = $"Modo offline. {OfflineCacheService.FormatSavedAt(cached.SavedAt)}";
        }
    }

    private void ApplyPackages(IReadOnlyList<TravelPackageDto> packages)
    {
        Packages.Clear();
        foreach (var package in packages)
        {
            Packages.Add(new PackageListItemViewModel(package));
        }

        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ShowInitialLoading));
    }

    protected override void OnLoadStateChanged()
    {
        OnPropertyChanged(nameof(ShowInitialLoading));
        OnPropertyChanged(nameof(ShowEmptyState));
    }
}
