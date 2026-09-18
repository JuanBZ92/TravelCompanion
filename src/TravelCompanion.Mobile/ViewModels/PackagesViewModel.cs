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
            MarkLastUpdated(cached.SavedAt);

            if (bootstrapStore.HasFreshSnapshot())
            {
                StatusMessage = null;
                return;
            }

            StatusMessage = OfflineCacheService.FormatSavedAt(cached.SavedAt);
        }

        try
        {
            var result = await bootstrapStore.RefreshResultAsync(token);
            if (result.IsUnauthorized)
            {
                authSessionService.Clear();
                await Shell.Current.GoToAsync("//login");
                return;
            }

            if (result.Value is { } bootstrap)
            {
                ApplyPackages(bootstrap.Packages);
                MarkLastUpdated(DateTimeOffset.UtcNow);
                StatusMessage = null;
            }
            else if (cached is null)
            {
                throw new HttpRequestException("No pudimos cargar los paquetes. Comprueba la conexión e inténtalo de nuevo.");
            }
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

    private void ApplyPackages(IReadOnlyList<TravelPackageDto>? packages)
    {
        Packages.Clear();
        foreach (var package in packages ?? [])
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
