namespace TravelCompanion.Shared;

public sealed record ProductAccessDefinition(
    ContentAccessLevel Level,
    string Label,
    string Description,
    bool CanBeRequiredByContent,
    bool CanBeGrantedToUser,
    bool IsCustomerProduct);

public static class ProductAccessModel
{
    private static readonly IReadOnlyList<ProductAccessDefinition> Definitions =
    [
        new(
            ContentAccessLevel.Free,
            "Gratis",
            "Contenido publico incluido para cualquier cuenta.",
            CanBeRequiredByContent: true,
            CanBeGrantedToUser: false,
            IsCustomerProduct: false),
        new(
            ContentAccessLevel.Paid,
            "Pago fijo",
            "Contenido desbloqueado por compra puntual o acceso explicito al destino.",
            CanBeRequiredByContent: true,
            CanBeGrantedToUser: true,
            IsCustomerProduct: true),
        new(
            ContentAccessLevel.Subscription,
            "Suscripcion",
            "Contenido disponible para usuarios con suscripcion activa.",
            CanBeRequiredByContent: true,
            CanBeGrantedToUser: true,
            IsCustomerProduct: true),
        new(
            ContentAccessLevel.Bundle,
            "Paquete",
            "Contenido incluido en un paquete reutilizable asignado al usuario.",
            CanBeRequiredByContent: true,
            CanBeGrantedToUser: true,
            IsCustomerProduct: true),
        new(
            ContentAccessLevel.AdminOnly,
            "Admin",
            "Contenido interno que no se expone en la app publica.",
            CanBeRequiredByContent: true,
            CanBeGrantedToUser: false,
            IsCustomerProduct: false)
    ];

    private static readonly IReadOnlyDictionary<ContentAccessLevel, ProductAccessDefinition> DefinitionByLevel =
        Definitions.ToDictionary(definition => definition.Level);

    public static IReadOnlyList<ProductAccessDefinition> All => Definitions;

    public static IEnumerable<ProductAccessDefinition> ContentAccessOptions =>
        Definitions.Where(definition => definition.CanBeRequiredByContent);

    public static IEnumerable<ProductAccessDefinition> UserGrantOptions =>
        Definitions.Where(definition => definition.CanBeGrantedToUser);

    public static ProductAccessDefinition Get(ContentAccessLevel level) =>
        DefinitionByLevel.TryGetValue(level, out var definition)
            ? definition
            : new ProductAccessDefinition(level, level.ToString(), "Nivel de acceso no reconocido.", false, false, false);

    public static string GetLabel(ContentAccessLevel level) => Get(level).Label;

    public static ContentAccessLevel GetPackageGrantLevel(bool isSubscription) =>
        isSubscription ? ContentAccessLevel.Subscription : ContentAccessLevel.Bundle;
}
