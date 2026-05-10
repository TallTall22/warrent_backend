using System.ComponentModel.DataAnnotations;

namespace WarrantApi.DTOs;

/// <summary>
/// POST /api/warrants/{id}/calculate 的請求 Body。
/// </summary>
public sealed class CalculateRequest
{
    /// <summary>標的市場價格，必須大於零</summary>
    [Required]
    public decimal MarketPrice { get; set; }
}
