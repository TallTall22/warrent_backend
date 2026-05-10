namespace WarrantApi.DTOs;

/// <summary>
/// GET /api/warrants 回傳的權證基本資料。
/// </summary>
public sealed class WarrantDto
{
    public string WarrantId { get; set; } = string.Empty;
    public decimal StrikePrice { get; set; }
    public decimal ConversionRatio { get; set; }
    public string WarrantType { get; set; } = string.Empty;
    public int PositionQty { get; set; }
}
