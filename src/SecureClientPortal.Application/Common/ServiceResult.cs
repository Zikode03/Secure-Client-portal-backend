namespace SecureClientPortal.Backend.Application.Common;

public sealed record ServiceResult<T>(
    T? Value = default,
    bool Forbidden = false,
    bool NotFound = false,
    bool Unauthorized = false,
    string? Error = null,
    string? ErrorCode = null,
    int? StatusCode = null)
{
    public static ServiceResult<T> Success(T value) => new(value);
    public static ServiceResult<T> ForbiddenResult(string? error = null, string? errorCode = null) => new(default, Forbidden: true, Error: error, ErrorCode: errorCode, StatusCode: 403);
    public static ServiceResult<T> NotFoundResult(string? error = null, string? errorCode = null) => new(default, NotFound: true, Error: error, ErrorCode: errorCode, StatusCode: 404);
    public static ServiceResult<T> UnauthorizedResult(string? error = null, string? errorCode = null, int statusCode = 401) => new(default, Unauthorized: true, Error: error, ErrorCode: errorCode, StatusCode: statusCode);
    public static ServiceResult<T> ErrorResult(string error, string? errorCode = null, int statusCode = 400) => new(default, Error: error, ErrorCode: errorCode, StatusCode: statusCode);
}
