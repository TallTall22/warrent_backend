namespace WarrantApi.Services;

/// <summary>
/// 純計算工具，封裝理論價值、Delta、避險數量公式。
/// WarrantService（試算 API）與 TrialLogService（存檔時重算）共用，確保計算邏輯單一來源。
/// </summary>
internal static class WarrantCalculator
{
    /// <summary>
    /// 計算 Delta 值與狀態。
    /// CALL：market &gt; strike → ITM；market = strike → ATM；market &lt; strike → OTM。
    /// PUT：反向判斷 ITM/OTM。
    /// </summary>
    internal static (decimal Delta, string DeltaStatus) CalculateDelta(
        string warrantType, decimal marketPrice, decimal strikePrice)
    {
        if (marketPrice == strikePrice)
            return (0.5m, "ATM");

        bool isCall = warrantType.Equals("CALL", StringComparison.OrdinalIgnoreCase);
        bool isITM  = isCall ? marketPrice > strikePrice : marketPrice < strikePrice;
        return isITM ? (0.8m, "ITM") : (0.2m, "OTM");
    }

    /// <summary>
    /// 計算理論價值，取 0 為下限。
    /// CALL = Max(0, (market - strike) × ratio)
    /// PUT  = Max(0, (strike - market) × ratio)
    /// </summary>
    internal static decimal CalculateTheoryPrice(
        string warrantType, decimal marketPrice, decimal strikePrice, decimal conversionRatio)
    {
        decimal intrinsicValue = warrantType.Equals("CALL", StringComparison.OrdinalIgnoreCase)
            ? marketPrice - strikePrice
            : strikePrice - marketPrice;

        return Math.Max(0m, intrinsicValue * conversionRatio);
    }

    /// <summary>
    /// 計算建議避險數量。
    /// hedgeQty = positionQty × conversionRatio × delta
    /// </summary>
    internal static decimal CalculateHedgeQty(
        int positionQty, decimal conversionRatio, decimal delta)
        => positionQty * conversionRatio * delta;
}
