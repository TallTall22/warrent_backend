namespace WarrantApi.Common;

/// <summary>
/// 通用結果模型，封裝業務操作的成功或失敗結果。
/// 使用 Result Pattern 取代例外驅動的錯誤處理。
/// </summary>
public sealed class Result<T>
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public T? Value { get; }
    public string ErrorMessage { get; }

    private Result(bool isSuccess, T? value, string errorMessage)
    {
        IsSuccess = isSuccess;
        Value = value;
        ErrorMessage = errorMessage;
    }

    /// <summary>建立成功結果</summary>
    public static Result<T> Success(T value)
        => new(true, value, string.Empty);

    /// <summary>建立失敗結果</summary>
    public static Result<T> Failure(string errorMessage)
        => new(false, default, errorMessage);
}

/// <summary>
/// 非泛型 Result，用於無回傳值的操作（如純寫入操作）。
/// </summary>
public sealed class Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public string ErrorMessage { get; }

    private Result(bool isSuccess, string errorMessage)
    {
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
    }

    public static Result Success()
        => new(true, string.Empty);

    public static Result Failure(string errorMessage)
        => new(false, errorMessage);
}
