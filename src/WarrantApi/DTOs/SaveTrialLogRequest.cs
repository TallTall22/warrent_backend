using System.ComponentModel.DataAnnotations;

namespace WarrantApi.DTOs;

/// <summary>
/// POST /api/warrants/{id}/trial-logs 的請求 Body。
/// 只需傳入市場價格；理論價值與避險數量由伺服器重新計算，確保金融資料完整性。
/// </summary>
public sealed class SaveTrialLogRequest
{
    /// <summary>標的市場價格，必須大於零</summary>
    [Required]
    [Range(typeof(decimal), "0.0001", "9999999999999999.9999",
        ErrorMessage = "標的價格必須大於零")]
    public decimal MarketPrice { get; set; }
}
