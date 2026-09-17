namespace TravelCompanion.Shared;

public static class GuidedTravelActions
{
    public const string Recommend = "recommend";
    public const string Alternative = "alternative";
}

public static class GuidedTravelCategories
{
    public const string Food = "food";
    public const string Relax = "relax";
    public const string Culture = "culture";
    public const string Walk = "walk";
    public const string Dance = "dance";
    public const string Nature = "nature";
    public const string Shopping = "shopping";
    public const string Viewpoint = "viewpoint";
    public const string Nightlife = "nightlife";

    public static bool IsValid(string? value) => value is
        Food or Relax or Culture or Walk or Dance or Nature or Shopping or Viewpoint or Nightlife;
}

public static class GuidedTravelPriorities
{
    public const string Direct = "direct";
    public const string Budget = "budget";
    public const string Distance = "distance";
    public const string Duration = "duration";

    public static bool IsValid(string? value) => value is Direct or Budget or Distance or Duration;
}
