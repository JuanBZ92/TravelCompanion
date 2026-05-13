using CommunityToolkit.Mvvm.ComponentModel;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed class PackageListItemViewModel(TravelPackageDto package) : ObservableObject
{
    public TravelPackageDto Package { get; } = package;
    public string Name => Package.Name;
    public string Description => Package.Description;
    public string Price => $"{Package.Currency} {Package.Price:0.00}";
    public string PackageType => Package.IsSubscription ? "Suscripcion" : "Pago fijo";
    public bool IsUnlocked => Package.IsUnlocked;
    public string AccessStatus => IsUnlocked ? "Incluido en tu cuenta" : "No incluido";
    public string RequiredAccess => GetAccessLevelText(Package.RequiredAccessLevel);

    private static string GetAccessLevelText(ContentAccessLevel accessLevel)
    {
        return accessLevel switch
        {
            ContentAccessLevel.Free => "Gratis",
            ContentAccessLevel.Paid => "Pago fijo",
            ContentAccessLevel.Subscription => "Suscripcion",
            ContentAccessLevel.Bundle => "Paquete",
            ContentAccessLevel.AdminOnly => "Admin",
            _ => accessLevel.ToString()
        };
    }
}
