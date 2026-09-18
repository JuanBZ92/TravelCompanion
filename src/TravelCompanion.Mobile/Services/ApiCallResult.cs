using System.Net;

namespace TravelCompanion.Mobile.Services;

public enum ApiCallStatus
{
    Success,
    Unauthorized,
    Forbidden,
    NotFound,
    TransientFailure,
    InvalidResponse
}

public sealed record ApiCallResult<T>(ApiCallStatus Status, T? Value = default, HttpStatusCode? StatusCode = null)
{
    public bool IsSuccess => Status == ApiCallStatus.Success && Value is not null;
    public bool IsUnauthorized => Status == ApiCallStatus.Unauthorized;

    public static ApiCallResult<T> Success(T value) => new(ApiCallStatus.Success, value, HttpStatusCode.OK);

    public static ApiCallResult<T> FromStatusCode(HttpStatusCode statusCode)
    {
        var status = statusCode switch
        {
            HttpStatusCode.Unauthorized => ApiCallStatus.Unauthorized,
            HttpStatusCode.Forbidden => ApiCallStatus.Forbidden,
            HttpStatusCode.NotFound => ApiCallStatus.NotFound,
            HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests => ApiCallStatus.TransientFailure,
            _ when (int)statusCode >= 500 => ApiCallStatus.TransientFailure,
            _ => ApiCallStatus.InvalidResponse
        };

        return new ApiCallResult<T>(status, StatusCode: statusCode);
    }

    public static ApiCallResult<T> TransientFailure() => new(ApiCallStatus.TransientFailure);
    public static ApiCallResult<T> InvalidResponse(HttpStatusCode? statusCode = null) =>
        new(ApiCallStatus.InvalidResponse, StatusCode: statusCode);
}
