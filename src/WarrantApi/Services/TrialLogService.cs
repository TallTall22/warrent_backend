using Microsoft.Data.SqlClient;
using WarrantApi.Common;
using WarrantApi.Domain.Entities;
using WarrantApi.DTOs;
using WarrantApi.Repositories.Interfaces;
using WarrantApi.Services.Interfaces;

namespace WarrantApi.Services;

/// <summary>
/// 避險試算記錄業務邏輯服務。
/// 負責試算記錄的冪等寫入與歷史查詢。
/// </summary>
public sealed class TrialLogService : ITrialLogService
{
    private readonly ITrialLogRepository _logRepo;
    private readonly ILogger<TrialLogService> _logger;

    // SQL Server unique constraint violation error numbers
    private const int SqlErrorUniqueConstraint = 2627;
    private const int SqlErrorUniqueIndex = 2601;

    public TrialLogService(ITrialLogRepository logRepo, ILogger<TrialLogService> logger)
    {
        _logRepo = logRepo;
        _logger  = logger;
    }

    /// <inheritdoc />
    public async Task<Result<TrialLogDto>> SaveAsync(
        string warrantId, Guid idempotencyKey, SaveTrialLogRequest request)
    {
        // Business rule: market price must be positive
        if (request.MarketPrice <= 0m)
            return Result<TrialLogDto>.Failure("標的價格必須大於零，禁止存檔");

        // Idempotency check: return existing record if already saved
        var existing = await _logRepo.FindByIdempotencyKeyAsync(idempotencyKey);
        if (existing is not null)
            return Result<TrialLogDto>.Success(MapToDto(existing));

        var log = new WarrantTrialLog
        {
            WarrantId      = warrantId,
            MarketPrice    = request.MarketPrice,
            TheoryPrice    = request.TheoryPrice,
            HedgeQty       = request.HedgeQty,
            CreatedTime    = DateTime.UtcNow,
            IdempotencyKey = idempotencyKey
        };

        try
        {
            var inserted = await _logRepo.InsertAsync(log);
            return Result<TrialLogDto>.Success(MapToDto(inserted));
        }
        catch (SqlException ex) when (
            ex.Number == SqlErrorUniqueConstraint || ex.Number == SqlErrorUniqueIndex)
        {
            // Race condition: another request with the same idempotency key committed first.
            // Fetch the already-committed record and return it as a successful idempotent response.
            _logger.LogWarning(
                "Idempotency key {IdempotencyKey} race condition detected, fetching committed record.",
                idempotencyKey);

            var committed = await _logRepo.FindByIdempotencyKeyAsync(idempotencyKey);
            if (committed is not null)
                return Result<TrialLogDto>.Success(MapToDto(committed));

            // Extremely unlikely: record was deleted between the two reads
            _logger.LogError(ex,
                "Race condition recovery failed: record for IdempotencyKey {IdempotencyKey} not found.",
                idempotencyKey);
            return Result<TrialLogDto>.Failure("存檔失敗，請稍後再試");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "存檔失敗 WarrantId={WarrantId} IdempotencyKey={IdempotencyKey}",
                warrantId, idempotencyKey);
            return Result<TrialLogDto>.Failure("存檔失敗，請稍後再試");
        }
    }

    /// <inheritdoc />
    public async Task<bool> IdempotencyKeyExistsAsync(Guid idempotencyKey)
    {
        var existing = await _logRepo.FindByIdempotencyKeyAsync(idempotencyKey);
        return existing is not null;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<TrialLogDto>> GetRecentLogsAsync(string warrantId)
    {
        var logs = await _logRepo.GetRecentByWarrantIdAsync(warrantId);
        return logs.Select(MapToDto);
    }

    // ── Mapping helper ─────────────────────────────────────────────────────────

    private static TrialLogDto MapToDto(WarrantTrialLog log) => new()
    {
        LogId       = log.LogId,
        WarrantId   = log.WarrantId,
        MarketPrice = log.MarketPrice,
        TheoryPrice = log.TheoryPrice,
        HedgeQty    = log.HedgeQty,
        CreatedTime = log.CreatedTime
    };
}
