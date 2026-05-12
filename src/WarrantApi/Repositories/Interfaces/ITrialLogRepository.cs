using WarrantApi.Domain.Entities;

namespace WarrantApi.Repositories.Interfaces;

/// <summary>
/// 避險試算記錄資料存取介面。
/// </summary>
public interface ITrialLogRepository
{
    /// <summary>
    /// 依冪等鍵查詢是否已有相同的試算記錄。
    /// </summary>
    /// <param name="idempotencyKey">冪等鍵（UUID）</param>
    /// <returns>找到的試算記錄，若不存在則回傳 null</returns>
    Task<WarrantTrialLog?> FindByIdempotencyKeyAsync(Guid idempotencyKey);

    /// <summary>
    /// 將試算記錄插入資料庫，並透過 SCOPE_IDENTITY() 取回產生的 LogId。
    /// </summary>
    /// <param name="log">待插入的試算記錄實體</param>
    /// <returns>含 LogId 的已插入記錄</returns>
    Task<WarrantTrialLog> InsertAsync(WarrantTrialLog log);

    /// <summary>
    /// 取得指定權證的最近 N 筆試算記錄，依 Log_ID 降冪排列。
    /// </summary>
    /// <param name="warrantId">權證代號</param>
    /// <param name="count">筆數上限（預設 10）</param>
    /// <returns>最近的試算記錄清單</returns>
    Task<IEnumerable<WarrantTrialLog>> GetRecentByWarrantIdAsync(string warrantId, int count = 10);
}
