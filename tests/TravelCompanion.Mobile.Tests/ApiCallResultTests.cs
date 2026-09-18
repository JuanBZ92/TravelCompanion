using System.Net;
using TravelCompanion.Mobile.Services;

namespace TravelCompanion.Mobile.Tests;

public sealed class ApiCallResultTests
{
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ApiCallStatus.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, ApiCallStatus.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, ApiCallStatus.NotFound)]
    [InlineData(HttpStatusCode.RequestTimeout, ApiCallStatus.TransientFailure)]
    [InlineData(HttpStatusCode.TooManyRequests, ApiCallStatus.TransientFailure)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ApiCallStatus.TransientFailure)]
    [InlineData(HttpStatusCode.BadRequest, ApiCallStatus.InvalidResponse)]
    public void Status_codes_are_classified_without_treating_transient_failures_as_unauthorized(
        HttpStatusCode statusCode,
        ApiCallStatus expected)
    {
        var result = ApiCallResult<object>.FromStatusCode(statusCode);

        Assert.Equal(expected, result.Status);
        Assert.Equal(expected == ApiCallStatus.Unauthorized, result.IsUnauthorized);
    }

    [Fact]
    public void Successful_result_exposes_its_value()
    {
        var value = new object();

        var result = ApiCallResult<object>.Success(value);

        Assert.True(result.IsSuccess);
        Assert.Same(value, result.Value);
    }
}
