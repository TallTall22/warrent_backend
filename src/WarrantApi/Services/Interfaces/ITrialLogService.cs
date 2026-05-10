using WarrantApi.Common;
using WarrantApi.DTOs;

namespace WarrantApi.Services.Interfaces;

/// <summary>
/// 避險試算記錄業務邏輯介面。
/// </summary>
public interface ITrialLogService
{
    /// <summary>
    /// 將試算結果存入資料庫，支援冪等保護。
    /// 相同 idempotencyKey 的重複請求直接回傳已存結果（IsNewRecord = false）。
    /// 全新寫入則回傳已插入記錄（IsNewRecord = true）。
    /// 透過 IsNewRecord 旗標讓 Controller 得以選擇正確的 HTTP 狀態碼，
    /// 無需額外查詢資料庫，消除 TOCTOU Race Condition。
    /// </summary>
    /// <param name="warrantId">權證代號</param>
    /// <param name="idempotencyKey">冪等鍵（UUID）</param>
    /// <param name="request">試算結果請求 Body</param>
    Task<Result<TrialLogSaveResult>> SaveAsync(
        string warrantId, Guid idempotencyKey, SaveTrialLogRequest request);

    /// <summary>
    /// 取得指定權證的最近 10 筆試算記錄。
    /// </summary>
    Task<IEnumerable<TrialLogDto>> GetRecentLogsAsync(string warrantId);
}
