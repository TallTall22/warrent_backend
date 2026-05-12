namespace WarrantApi.DTOs;

/// <summary>
/// 試算記錄回傳 DTO，對應 GET /api/warrants/{id}/trial-logs 的單筆記錄。
/// </summary>
public sealed class TrialLogDto
{
    public int LogId { get; set; }
    public string WarrantId { get; set; } = string.Empty;
    public decimal MarketPrice { get; set; }
    public decimal TheoryPrice { get; set; }
    public decimal HedgeQty { get; set; }
    public DateTime CreatedTime { get; set; }
}
