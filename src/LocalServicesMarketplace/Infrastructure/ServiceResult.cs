namespace LocalServicesMarketplace.Infrastructure;

public enum ServiceErrorKind
{
    None = 0,
    Validation,
    NotFound,
    Forbidden,
    Conflict
}

/// <summary>Outcome of a business operation. Controllers translate it to redirects / HTTP status codes.</summary>
public class ServiceResult
{
    public bool Succeeded { get; protected init; }
    public string? Error { get; protected init; }
    public ServiceErrorKind ErrorKind { get; protected init; }

    public static ServiceResult Ok() => new() { Succeeded = true };
    public static ServiceResult Fail(string error, ServiceErrorKind kind = ServiceErrorKind.Validation) =>
        new() { Succeeded = false, Error = error, ErrorKind = kind };
    public static ServiceResult NotFound(string error = "The requested item was not found.") => Fail(error, ServiceErrorKind.NotFound);
    public static ServiceResult Forbidden(string error = "You are not allowed to perform this action.") => Fail(error, ServiceErrorKind.Forbidden);
    public static ServiceResult Conflict(string error) => Fail(error, ServiceErrorKind.Conflict);
}

public class ServiceResult<T> : ServiceResult
{
    public T? Value { get; private init; }

    public static ServiceResult<T> Ok(T value) => new() { Succeeded = true, Value = value };
    public new static ServiceResult<T> Fail(string error, ServiceErrorKind kind = ServiceErrorKind.Validation) =>
        new() { Succeeded = false, Error = error, ErrorKind = kind };
    public new static ServiceResult<T> NotFound(string error = "The requested item was not found.") => Fail(error, ServiceErrorKind.NotFound);
    public new static ServiceResult<T> Forbidden(string error = "You are not allowed to perform this action.") => Fail(error, ServiceErrorKind.Forbidden);
    public new static ServiceResult<T> Conflict(string error) => Fail(error, ServiceErrorKind.Conflict);
}

public class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 10;
    public int TotalCount { get; init; }
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;
}
