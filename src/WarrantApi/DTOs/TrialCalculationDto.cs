namespace WarrantApi.DTOs;

/// <summary>
/// POST /api/warrants/{id}/calculate 回傳的試算結果。
/// 包含 Delta 狀態、理論價值、建議避險數量。
/// </summary>
public sealed class TrialCalculationDto
{
    public string WarrantId { get; set; } = string.Empty;
    public decimal MarketPrice { get; set; }
    public decimal StrikePrice { get; set; }
    public decimal ConversionRatio { get; set; }
    public string WarrantType { get; set; } = string.Empty;
    public int PositionQty { get; set; }

    /// <summary>Delta 值：ITM=0.8, ATM=0.5, OTM=0.2</summary>
    public decimal Delta { get; set; }

    /// <summary>Delta 狀態描述：ITM / ATM / OTM</summary>
    public string DeltaStatus { get; set; } = string.Empty;

    /// <summary>權證理論價值</summary>
    public decimal TheoryPrice { get; set; }

    /// <summary>建議避險數量</summary>
    public decimal HedgeQty { get; set; }
}
