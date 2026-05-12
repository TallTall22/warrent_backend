using System.Data;
using Microsoft.Data.SqlClient;

namespace WarrantApi.Infrastructure;

/// <summary>
/// Dapper 連線工廠。
/// 提供 IDbConnection 給 Repository 層使用，不依賴 EF Core。
/// 使用 Singleton 生命週期管理 connectionString，每次呼叫 CreateConnection() 產生新連線。
/// </summary>
public sealed class AppDbContext
{
    private readonly string _connectionString;

    public AppDbContext(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "連線字串 'DefaultConnection' 未設定，請檢查 appsettings.json。");
    }

    /// <summary>
    /// 建立並回傳一個新的 SQL Server 資料庫連線。
    /// 呼叫端負責 Dispose（建議使用 using 陳述式）。
    /// </summary>
    public IDbConnection CreateConnection()
        => new SqlConnection(_connectionString);
}
