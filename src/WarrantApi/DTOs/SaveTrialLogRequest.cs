using System.ComponentModel.DataAnnotations;

namespace WarrantApi.DTOs;

/// <summary>
/// POST /api/warrants/{id}/trial-logs 的請求 Body。
/// 將試算結果寫入 Warrant_Trial_Log 資料表。
/// </summary>
public sealed class SaveTrialLogRequest
{
    /// <summary>標的市場價格，必須大於零</summary>
    [Required]
    [Range(0.0001, double.MaxValue, ErrorMessage = "標的價格必須大於零")]
    public decimal MarketPrice { get; set; }

    /// <summary>權證理論價值，不得為負數</summary>
    [Required]
    [Range(0, double.MaxValue, ErrorMessage = "理論價值不得為負數")]
    public decimal TheoryPrice { get; set; }

    /// <summary>建議避險數量，不得為負數</summary>
    [Required]
    [Range(0, double.MaxValue, ErrorMessage = "避險數量不得為負數")]
    public decimal HedgeQty { get; set; }
}
