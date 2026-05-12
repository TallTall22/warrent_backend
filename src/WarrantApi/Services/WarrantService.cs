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
    private readonly ILogger<WarrantService> _logger;

    public WarrantService(IWarrantRepository warrantRepo, ILogger<WarrantService> logger)
    {
        _warrantRepo = warrantRepo;
        _logger      = logger;
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
        {
            _logger.LogWarning(
                "試算失敗：非法價格 WarrantId={WarrantId} MarketPrice={MarketPrice}",
                warrantId, marketPrice);
            return Result<TrialCalculationDto>.Failure("標的價格必須大於零");
        }

        var warrant = await _warrantRepo.GetByIdAsync(warrantId);
        if (warrant is null)
        {
            _logger.LogWarning("試算失敗：找不到權證 WarrantId={WarrantId}", warrantId);
            return Result<TrialCalculationDto>.Failure("找不到指定的權證");
        }

        var (delta, deltaStatus) = WarrantCalculator.CalculateDelta(
            warrant.WarrantType, marketPrice, warrant.StrikePrice);

        var theoryPrice = WarrantCalculator.CalculateTheoryPrice(
            warrant.WarrantType, marketPrice, warrant.StrikePrice, warrant.ConversionRatio);

        var hedgeQty = WarrantCalculator.CalculateHedgeQty(
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

        _logger.LogInformation(
            "試算完成 WarrantId={WarrantId} MarketPrice={MarketPrice} Delta={Delta} TheoryPrice={TheoryPrice}",
            warrantId, marketPrice, delta, theoryPrice);

        return Result<TrialCalculationDto>.Success(dto);
    }

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
