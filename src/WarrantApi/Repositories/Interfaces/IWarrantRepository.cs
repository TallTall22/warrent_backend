using WarrantApi.Domain.Entities;

namespace WarrantApi.Repositories.Interfaces;

/// <summary>
/// 權證主檔資料存取介面。
/// </summary>
public interface IWarrantRepository
{
    /// <summary>
    /// 取得權證分頁清單，支援關鍵字前綴搜尋。
    /// </summary>
    /// <param name="keyword">搜尋關鍵字（前綴比對，null 表示不過濾）</param>
    /// <param name="page">頁碼（從 1 開始）</param>
    /// <param name="pageSize">每頁筆數</param>
    /// <returns>符合條件的權證清單與總筆數</returns>
    Task<(IEnumerable<WarrantMaster> Items, int Total)> GetListAsync(
        string? keyword, int page, int pageSize);

    /// <summary>
    /// 依權證代號取得單筆權證主檔。
    /// </summary>
    /// <param name="warrantId">權證代號</param>
    /// <returns>找到的權證主檔，若不存在則回傳 null</returns>
    Task<WarrantMaster?> GetByIdAsync(string warrantId);
}
