namespace TravelCompanion.Api.Services;

public sealed class DayPlanException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
