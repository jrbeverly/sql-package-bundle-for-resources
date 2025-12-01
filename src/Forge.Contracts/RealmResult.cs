namespace Forge.Contracts;

// A value or a failure: never both (HLD.md, Forge.Contracts). Failures carry
// the error code and a message naming what to fix; they are values, not
// exceptions, and never cross a project boundary as exceptions.
public sealed record RealmResult<T>(T? Value, RealmErrorCode? ErrorCode, string? Message)
{
    public static RealmResult<T> Success(T value) => new(value, null, null);

    public static RealmResult<T> Failure(RealmErrorCode errorCode, string message) => new(default, errorCode, message);
}
