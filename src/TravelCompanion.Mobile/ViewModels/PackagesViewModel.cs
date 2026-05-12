using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class PackagesViewModel(TravelCompanionApiClient apiClient) : ViewModelBase
{
    public ObservableCollection<TravelPackageDto> Packages { get; } = [];

    [RelayCommand]
    private Task LoadPackagesAsync()
    {
        return LoadAsync(async () =>
        {
            Packages.Clear();
            var packages = await apiClient.GetPackagesAsync();
            foreach (var package in packages)
            {
                Packages.Add(package);
            }
        });
    }
}
