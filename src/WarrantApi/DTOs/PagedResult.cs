namespace WarrantApi.DTOs;

/// <summary>
/// 通用分頁回傳模型。
/// 用於分頁 API 回傳，包含資料集合、總筆數、當前頁碼與每頁筆數。
/// </summary>
public sealed class PagedResult<T>
{
    /// <summary>當前頁的資料集合</summary>
    public IReadOnlyList<T> Data { get; set; } = [];

    /// <summary>資料總筆數（不分頁）</summary>
    public int Total { get; set; }

    /// <summary>當前頁碼（從 1 開始）</summary>
    public int Page { get; set; }

    /// <summary>每頁筆數</summary>
    public int PageSize { get; set; }
}
