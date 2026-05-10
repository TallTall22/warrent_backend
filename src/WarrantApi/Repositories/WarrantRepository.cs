using Dapper;
using WarrantApi.Domain.Entities;
using WarrantApi.Infrastructure;
using WarrantApi.Repositories.Interfaces;

namespace WarrantApi.Repositories;

/// <summary>
/// 權證主檔 Repository 實作，使用 Dapper 進行資料存取。
/// 所有查詢均為 Set-based，禁止在 Loop 內呼叫資料庫。
/// </summary>
public sealed class WarrantRepository : IWarrantRepository
{
    private readonly AppDbContext _db;

    public WarrantRepository(AppDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<(IEnumerable<WarrantMaster> Items, int Total)> GetListAsync(
        string? keyword, int page, int pageSize)
    {
        // Single round-trip: COUNT + paged SELECT in one QueryMultipleAsync call
        const string sql = """
            SELECT COUNT(*)
            FROM Warrant_Master
            WHERE (@Keyword IS NULL OR Warrant_ID LIKE @Keyword + '%');

            SELECT Warrant_ID   AS WarrantId,
                   Strike_Price AS StrikePrice,
                   Conversion_Ratio AS ConversionRatio,
                   Warrant_Type AS WarrantType,
                   Position_Qty AS PositionQty
            FROM Warrant_Master
            WHERE (@Keyword IS NULL OR Warrant_ID LIKE @Keyword + '%')
            ORDER BY Warrant_ID
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;

        using var conn = _db.CreateConnection();
        conn.Open();

        using var multi = await conn.QueryMultipleAsync(sql, new
        {
            Keyword = string.IsNullOrWhiteSpace(keyword) ? null : keyword,
            Offset = (page - 1) * pageSize,
            PageSize = pageSize
        });

        var total = await multi.ReadFirstAsync<int>();
        var items = await multi.ReadAsync<WarrantMaster>();

        return (items, total);
    }

    /// <inheritdoc />
    public async Task<WarrantMaster?> GetByIdAsync(string warrantId)
    {
        const string sql = """
            SELECT Warrant_ID   AS WarrantId,
                   Strike_Price AS StrikePrice,
                   Conversion_Ratio AS ConversionRatio,
                   Warrant_Type AS WarrantType,
                   Position_Qty AS PositionQty
            FROM Warrant_Master
            WHERE Warrant_ID = @WarrantId;
            """;

        using var conn = _db.CreateConnection();
        conn.Open();

        return await conn.QueryFirstOrDefaultAsync<WarrantMaster>(sql, new { WarrantId = warrantId });
    }
}
