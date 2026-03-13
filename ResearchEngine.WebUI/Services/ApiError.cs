using ResearchEngine.WebUI.Api;

namespace ResearchEngine.WebUI.Services;

public sealed record ApiError(ApiErrorKind Kind, string Message, int? StatusCode = null, string? Details = null);

public enum ApiErrorKind
{
    Network,
    Http,
    Validation,
    Auth,
    Unexpected
}

public readonly record struct ApiResult<T>(T? Data, ApiError? Error)
{
    public bool IsSuccess => Error is null;

    public static ApiResult<T> Ok(T data) => new(data, null);
    public static ApiResult<T> Fail(ApiError error) => new(default, error);
}

public static class ApiErrorMapper
{
    public static ApiError Map(Exception ex)
    {
        if (ex is ApiException apiEx)
        {
            var status = apiEx.StatusCode;
            var details = apiEx.Response;

            if (status is 401 or 403)
                return new ApiError(ApiErrorKind.Auth, "Authentication/authorization error.", status, details);

            if (status is 400 or 422)
                return new ApiError(ApiErrorKind.Validation, "Request validation failed.", status, details);

            return new ApiError(ApiErrorKind.Http, $"Server returned HTTP {status}.", status, details);
        }

        // Network-ish failures
        if (ex is HttpRequestException or TaskCanceledException)
        {
            return new ApiError(ApiErrorKind.Network, "Network error while calling the API.", null, ex.Message);
        }

        return new ApiError(ApiErrorKind.Unexpected, "Unexpected error.", null, ex.Message);
    }
}