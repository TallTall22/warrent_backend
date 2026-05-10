namespace WarrantApi.Domain.Entities;

/// <summary>
/// 權證主檔實體，對應資料庫 Warrant_Master 表。
/// 所有金融數值欄位使用 decimal，確保精度無損。
/// </summary>
public sealed class WarrantMaster
{
    /// <summary>權證代號 — Warrant_ID VARCHAR(10)</summary>
    public string WarrantId { get; set; } = string.Empty;

    /// <summary>履約價格 — Strike_Price DECIMAL(18,4)</summary>
    public decimal StrikePrice { get; set; }

    /// <summary>行使比例（幾股換一張）— Conversion_Ratio DECIMAL(18,4)</summary>
    public decimal ConversionRatio { get; set; }

    /// <summary>權證類型 CALL/PUT — Warrant_Type VARCHAR(4)</summary>
    public string WarrantType { get; set; } = string.Empty;

    /// <summary>發行部位數量 — Position_Qty INT</summary>
    public int PositionQty { get; set; }
}
