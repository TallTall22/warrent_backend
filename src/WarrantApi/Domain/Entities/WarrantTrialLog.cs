namespace WarrantApi.Domain.Entities;

/// <summary>
/// 避險試算記錄實體，對應資料庫 Warrant_Trial_Log 表。
/// 所有金融數值欄位使用 decimal，確保精度無損。
/// </summary>
public sealed class WarrantTrialLog
{
    /// <summary>試算記錄流水號 — Log_ID INT IDENTITY</summary>
    public int LogId { get; set; }

    /// <summary>權證代號 — Warrant_ID VARCHAR(10)</summary>
    public string WarrantId { get; set; } = string.Empty;

    /// <summary>標的市場價格 — Market_Price DECIMAL(18,4)</summary>
    public decimal MarketPrice { get; set; }

    /// <summary>權證理論價值 — Theory_Price DECIMAL(18,4)</summary>
    public decimal TheoryPrice { get; set; }

    /// <summary>建議避險數量 — Hedge_Qty DECIMAL(18,2)</summary>
    public decimal HedgeQty { get; set; }

    /// <summary>試算建立時間 — Created_Time DATETIME</summary>
    public DateTime CreatedTime { get; set; }

    /// <summary>冪等鍵，防止重複寫入 — Idempotency_Key UNIQUEIDENTIFIER</summary>
    public Guid IdempotencyKey { get; set; }
}
