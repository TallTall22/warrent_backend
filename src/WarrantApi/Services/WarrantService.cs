using WarrantApi.Common;
using WarrantApi.Domain.Entities;
using WarrantApi.DTOs;
using WarrantApi.Repositories.Interfaces;
using WarrantApi.Services.Interfaces;

namespace WarrantApi.Services;

/// <summary>
/// 權證業務邏輯服務，包含清單查詢、單筆查詢與避險試算計算。
/// 純計算邏輯（CalculateAsync）不寫入資料庫，由 TrialLogService 負責持久化。
/// </summary>
public sealed class WarrantService : IWarrantService
{
    private readonly IWarrantRepository _warrantRepo;

    public WarrantService(IWarrantRepository warrantRepo)
    {
        _warrantRepo = warrantRepo;
    }

    /// <inheritdoc />
    public async Task<PagedResult<WarrantDto>> GetWarrantListAsync(
        string? keyword, int page, int pageSize)
    {
        var (items, total) = await _warrantRepo.GetListAsync(keyword, page, pageSize);

        return new PagedResult<WarrantDto>
        {
            Data = items.Select(MapToDto).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    /// <inheritdoc />
    public async Task<WarrantMaster?> GetWarrantByIdAsync(string warrantId)
        => await _warrantRepo.GetByIdAsync(warrantId);

    /// <inheritdoc />
    public async Task<Result<TrialCalculationDto>> CalculateAsync(
        string warrantId, decimal marketPrice)
    {
        if (marketPrice <= 0m)
            return Result<TrialCalculationDto>.Failure("標的價格必須大於零");

        var warrant = await _warrantRepo.GetByIdAsync(warrantId);
        if (warrant is null)
            return Result<TrialCalculationDto>.Failure("找不到指定的權證");

        var (delta, deltaStatus) = CalculateDelta(
            warrant.WarrantType, marketPrice, warrant.StrikePrice);

        var theoryPrice = CalculateTheoryPrice(
            warrant.WarrantType, marketPrice, warrant.StrikePrice, warrant.ConversionRatio);

        var hedgeQty = CalculateHedgeQty(
            warrant.PositionQty, warrant.ConversionRatio, delta);

        var dto = new TrialCalculationDto
        {
            WarrantId       = warrant.WarrantId,
            MarketPrice     = marketPrice,
            StrikePrice     = warrant.StrikePrice,
            ConversionRatio = warrant.ConversionRatio,
            WarrantType     = warrant.WarrantType,
            PositionQty     = warrant.PositionQty,
            Delta           = delta,
            DeltaStatus     = deltaStatus,
            TheoryPrice     = theoryPrice,
            HedgeQty        = hedgeQty
        };

        return Result<TrialCalculationDto>.Success(dto);
    }

    // ── Private calculation helpers ────────────────────────────────────────────

    /// <summary>
    /// 計算 Delta 值與狀態（ITM / ATM / OTM）。
    /// CALL：市價 > 履約價 → ITM；市價 = 履約價 → ATM；市價 < 履約價 → OTM
    /// PUT：市價 < 履約價 → ITM；市價 = 履約價 → ATM；市價 > 履約價 → OTM
    /// </summary>
    private static (decimal delta, string deltaStatus) CalculateDelta(
        string warrantType, decimal marketPrice, decimal strikePrice)
    {
        bool isCall = warrantType.Equals("CALL", StringComparison.OrdinalIgnoreCase);

        if (marketPrice == strikePrice)
            return (0.5m, "ATM");

        bool isITM = isCall ? marketPrice > strikePrice : marketPrice < strikePrice;
        return isITM ? (0.8m, "ITM") : (0.2m, "OTM");
    }

    /// <summary>
    /// 計算權證理論價值，不得為負數（取 0 為下限）。
    /// </summary>
    private static decimal CalculateTheoryPrice(
        string warrantType, decimal marketPrice, decimal strikePrice, decimal conversionRatio)
    {
        decimal intrinsicValue = warrantType.Equals("CALL", StringComparison.OrdinalIgnoreCase)
            ? marketPrice - strikePrice
            : strikePrice - marketPrice;

        return Math.Max(0m, intrinsicValue * conversionRatio);
    }

    /// <summary>
    /// 計算建議避險數量。
    /// </summary>
    private static decimal CalculateHedgeQty(int positionQty, decimal conversionRatio, decimal delta)
        => positionQty * conversionRatio * delta;

    // ── Mapping helpers ────────────────────────────────────────────────────────

    private static WarrantDto MapToDto(WarrantMaster w) => new()
    {
        WarrantId       = w.WarrantId,
        StrikePrice     = w.StrikePrice,
        ConversionRatio = w.ConversionRatio,
        WarrantType     = w.WarrantType,
        PositionQty     = w.PositionQty
    };
}
