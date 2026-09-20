namespace TravelCompanion.Api.Services;

internal static class ProductLanguage
{
    public static bool IsSpanish(HttpContext context)
    {
        var language = context.Features
            .Get<Microsoft.AspNetCore.Localization.IRequestCultureFeature>()?
            .RequestCulture.UICulture.TwoLetterISOLanguageName;
        if (string.IsNullOrWhiteSpace(language))
            language = context.Request.Headers.AcceptLanguage.FirstOrDefault();
        return language?.StartsWith("es", StringComparison.OrdinalIgnoreCase) == true;
    }

    public static string Text(HttpContext context, string english, string spanish) =>
        IsSpanish(context) ? spanish : english;
}
