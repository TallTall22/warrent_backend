namespace WarrantApi.DTOs;

/// <summary>
/// SaveAsync 的複合回傳結果，封裝試算記錄 DTO 與是否為新寫入的旗標。
/// 將「是否新記錄」的判斷移入 Service 層，消除 Controller 的 TOCTOU Race Condition。
/// </summary>
public sealed class TrialLogSaveResult
{
    /// <summary>試算記錄 DTO</summary>
    public required TrialLogDto Log { get; init; }

    /// <summary>
    /// true  → 此次為全新寫入（Controller 應回傳 HTTP 201 Created）
    /// false → 冪等重複請求或 Race Condition 後查回的已存記錄（HTTP 200 OK）
    /// </summary>
    public bool IsNewRecord { get; init; }
}
