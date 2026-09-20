namespace TravelCompanion.Shared;

public enum ItineraryItemOwner
{
    Yuku,
    Traveler
}

public enum ItineraryItemSource
{
    Manual,
    YukuRecommendation,
    GooglePlace
}

public enum ItineraryTimePrecision
{
    PeriodOnly,
    Exact
}

public enum BuilderAccessStatus
{
    Trial,
    PurchasePending,
    Active,
    Expired,
    Revoked
}

public enum ItineraryFlexibility
{
    Flexible,
    FixedByTraveler,
    ConfirmedReservation
}
