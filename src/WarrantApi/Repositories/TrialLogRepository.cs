using Dapper;
using WarrantApi.Domain.Entities;
using WarrantApi.Infrastructure;
using WarrantApi.Repositories.Interfaces;

namespace WarrantApi.Repositories;

/// <summary>
/// 避險試算記錄 Repository 實作，使用 Dapper 進行資料存取。
/// </summary>
public sealed class TrialLogRepository : ITrialLogRepository
{
    private readonly AppDbContext _db;

    public TrialLogRepository(AppDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<WarrantTrialLog?> FindByIdempotencyKeyAsync(Guid idempotencyKey)
    {
        const string sql = """
            SELECT Log_ID           AS LogId,
                   Warrant_ID       AS WarrantId,
                   Market_Price     AS MarketPrice,
                   Theory_Price     AS TheoryPrice,
                   Hedge_Qty        AS HedgeQty,
                   Created_Time     AS CreatedTime,
                   Idempotency_Key  AS IdempotencyKey
            FROM Warrant_Trial_Log
            WHERE Idempotency_Key = @IdempotencyKey;
            """;

        using var conn = _db.CreateConnection();

        return await conn.QueryFirstOrDefaultAsync<WarrantTrialLog>(sql, new { IdempotencyKey = idempotencyKey });
    }

    /// <inheritdoc />
    public async Task<WarrantTrialLog> InsertAsync(WarrantTrialLog log)
    {
        const string sql = """
            INSERT INTO Warrant_Trial_Log
                (Warrant_ID, Market_Price, Theory_Price, Hedge_Qty, Created_Time, Idempotency_Key)
            VALUES
                (@WarrantId, @MarketPrice, @TheoryPrice, @HedgeQty, @CreatedTime, @IdempotencyKey);
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """;

        using var conn = _db.CreateConnection();

        var logId = await conn.ExecuteScalarAsync<int>(sql, new
        {
            log.WarrantId,
            log.MarketPrice,
            log.TheoryPrice,
            log.HedgeQty,
            log.CreatedTime,
            log.IdempotencyKey
        });

        return new WarrantTrialLog
        {
            LogId          = logId,
            WarrantId      = log.WarrantId,
            MarketPrice    = log.MarketPrice,
            TheoryPrice    = log.TheoryPrice,
            HedgeQty       = log.HedgeQty,
            CreatedTime    = log.CreatedTime,
            IdempotencyKey = log.IdempotencyKey
        };
    }

    /// <inheritdoc />
    public async Task<IEnumerable<WarrantTrialLog>> GetRecentByWarrantIdAsync(
        string warrantId, int count = 10)
    {
        const string sql = """
            SELECT TOP (@Count)
                   Log_ID           AS LogId,
                   Warrant_ID       AS WarrantId,
                   Market_Price     AS MarketPrice,
                   Theory_Price     AS TheoryPrice,
                   Hedge_Qty        AS HedgeQty,
                   Created_Time     AS CreatedTime
            FROM Warrant_Trial_Log
            WHERE Warrant_ID = @WarrantId
            ORDER BY Log_ID DESC;
            """;

        using var conn = _db.CreateConnection();

        return await conn.QueryAsync<WarrantTrialLog>(sql, new { WarrantId = warrantId, Count = count });
    }
}
