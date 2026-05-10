using WarrantApi.Common;
using WarrantApi.Domain.Entities;
using WarrantApi.DTOs;

namespace WarrantApi.Services.Interfaces;

/// <summary>
/// 權證業務邏輯介面。
/// </summary>
public interface IWarrantService
{
    /// <summary>
    /// 取得權證分頁清單，支援關鍵字前綴搜尋。
    /// </summary>
    Task<PagedResult<WarrantDto>> GetWarrantListAsync(string? keyword, int page, int pageSize);

    /// <summary>
    /// 依權證代號取得單筆權證主檔，不存在則回傳 null。
    /// </summary>
    Task<WarrantMaster?> GetWarrantByIdAsync(string warrantId);

    /// <summary>
    /// 執行避險試算計算（純計算，不寫入資料庫）。
    /// </summary>
    /// <param name="warrantId">權證代號</param>
    /// <param name="marketPrice">標的市場價格（必須大於零）</param>
    Task<Result<TrialCalculationDto>> CalculateAsync(string warrantId, decimal marketPrice);
}
