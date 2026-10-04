namespace LeaveManagement.Api.Services;

public enum ErrorKind
{
    None,
    NotFound,
    BadRequest,
    Conflict
}

public sealed class ServiceResult<T>
{
    public ErrorKind Kind { get; private init; }
    public T? Value { get; private init; }
    public string? Error { get; private init; }

    public bool IsSuccess => Kind == ErrorKind.None;

    public static ServiceResult<T> Ok(T value) => new()
    {
        Kind = ErrorKind.None,
        Value = value
    };

    public static ServiceResult<T> Fail(ErrorKind kind, string error) => new()
    {
        Kind = kind,
        Error = error
    };
}
