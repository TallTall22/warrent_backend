using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WarrantApi.Domain.Entities;
using WarrantApi.DTOs;
using WarrantApi.Repositories.Interfaces;
using WarrantApi.Services;

namespace WarrantApi.Tests.Services;

/// <summary>
/// TrialLogService.SaveAsync 單元測試。
/// 涵蓋：輸入驗證、warrantId 存在性、冪等重複、新記錄寫入、Race Condition 處理。
/// </summary>
public sealed class TrialLogServiceTests
{
    // ── Test fixture helpers ───────────────────────────────────────────────────

    private static TrialLogService CreateService(
        ITrialLogRepository logRepo,
        IWarrantRepository? warrantRepo = null)
    {
        warrantRepo ??= BuildDefaultWarrantRepo().Object;
        return new(logRepo, warrantRepo, NullLogger<TrialLogService>.Instance);
    }

    private static Mock<IWarrantRepository> BuildDefaultWarrantRepo()
    {
        var mock = new Mock<IWarrantRepository>();
        mock.Setup(r => r.GetByIdAsync(It.IsAny<string>()))
            .ReturnsAsync(new WarrantMaster
            {
                WarrantId       = "TW1234",
                WarrantType     = "CALL",
                StrikePrice     = 100m,
                ConversionRatio = 0.1m,
                PositionQty     = 5000
            });
        return mock;
    }

    // SaveTrialLogRequest 現在只需要 MarketPrice（理論價值與避險數量由伺服器計算）
    private static SaveTrialLogRequest BuildValidRequest(decimal marketPrice = 105m) => new()
    {
        MarketPrice = marketPrice
    };

    private static WarrantTrialLog BuildExistingLog(Guid idempotencyKey) => new()
    {
        LogId          = 42,
        WarrantId      = "TW1234",
        MarketPrice    = 105m,
        TheoryPrice    = 0.5m,
        HedgeQty       = 400m,
        CreatedTime    = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        IdempotencyKey = idempotencyKey
    };

    // ── 1. marketPrice <= 0 → Failure，不呼叫任何 Repository ────────────────

    [Fact]
    public async Task SaveAsync_InvalidMarketPrice_ReturnsFailureWithoutDbCall()
    {
        // Arrange: MockBehavior.Strict 確保任何 DB 呼叫都會拋出例外
        var mockLogRepo     = new Mock<ITrialLogRepository>(MockBehavior.Strict);
        var mockWarrantRepo = new Mock<IWarrantRepository>(MockBehavior.Strict);
        var service = CreateService(mockLogRepo.Object, mockWarrantRepo.Object);
        var request = BuildValidRequest(marketPrice: 0m);

        // Act
        var result = await service.SaveAsync("TW1234", Guid.NewGuid(), request);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("大於零", result.ErrorMessage);
        mockLogRepo.VerifyNoOtherCalls();
        mockWarrantRepo.VerifyNoOtherCalls(); // 非法價格時連 warrantId 驗證都不應執行
    }

    // ── 2. warrantId 不存在 → Failure ─────────────────────────────────────────

    [Fact]
    public async Task SaveAsync_WarrantNotFound_ReturnsFailure()
    {
        var mockLogRepo     = new Mock<ITrialLogRepository>();
        var mockWarrantRepo = new Mock<IWarrantRepository>();

        // 冪等查詢先執行：回傳 null（新請求）
        mockLogRepo
            .Setup(r => r.FindByIdempotencyKeyAsync(It.IsAny<Guid>()))
            .ReturnsAsync((WarrantTrialLog?)null);

        mockWarrantRepo
            .Setup(r => r.GetByIdAsync("NOTEXIST"))
            .ReturnsAsync((WarrantMaster?)null);

        var service = CreateService(mockLogRepo.Object, mockWarrantRepo.Object);
        var result  = await service.SaveAsync("NOTEXIST", Guid.NewGuid(), BuildValidRequest());

        Assert.True(result.IsFailure);
        Assert.Contains("找不到", result.ErrorMessage);
        // warrant 不存在時不應呼叫 InsertAsync
        mockLogRepo.Verify(r => r.InsertAsync(It.IsAny<WarrantTrialLog>()), Times.Never);
    }

    // ── 3. 冪等重複（key 已存在）→ Success, IsNewRecord=false，不呼叫 InsertAsync ─

    [Fact]
    public async Task SaveAsync_DuplicateIdempotencyKey_ReturnsExistingWithIsNewRecordFalse()
    {
        // Arrange
        var idempotencyKey = Guid.NewGuid();
        var existingLog    = BuildExistingLog(idempotencyKey);

        var mockRepo = new Mock<ITrialLogRepository>();
        mockRepo
            .Setup(r => r.FindByIdempotencyKeyAsync(idempotencyKey))
            .ReturnsAsync(existingLog);

        var service = CreateService(mockRepo.Object);
        var request = BuildValidRequest();

        // Act
        var result = await service.SaveAsync("TW1234", idempotencyKey, request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsNewRecord);
        Assert.Equal(existingLog.LogId, result.Value.Log.LogId);
        Assert.Equal(existingLog.WarrantId, result.Value.Log.WarrantId);

        // 確認 InsertAsync 從未被呼叫
        mockRepo.Verify(r => r.InsertAsync(It.IsAny<WarrantTrialLog>()), Times.Never);
    }

    // ── 4. 新記錄 → Success, IsNewRecord=true，呼叫 InsertAsync 一次 ──────────

    [Fact]
    public async Task SaveAsync_NewRecord_ReturnsInsertedWithIsNewRecordTrue()
    {
        // Arrange
        var idempotencyKey = Guid.NewGuid();
        var insertedLog = new WarrantTrialLog
        {
            LogId          = 99,
            WarrantId      = "TW1234",
            MarketPrice    = 105m,
            TheoryPrice    = 0.5m,   // CALL ITM: (105-100)*0.1 = 0.5
            HedgeQty       = 400m,   // 5000 * 0.1 * 0.8 = 400
            CreatedTime    = DateTime.UtcNow,
            IdempotencyKey = idempotencyKey
        };

        var mockRepo = new Mock<ITrialLogRepository>();
        mockRepo
            .Setup(r => r.FindByIdempotencyKeyAsync(idempotencyKey))
            .ReturnsAsync((WarrantTrialLog?)null); // 冪等 key 不存在，為新記錄

        mockRepo
            .Setup(r => r.InsertAsync(It.IsAny<WarrantTrialLog>()))
            .ReturnsAsync(insertedLog);

        var service = CreateService(mockRepo.Object);
        var request = BuildValidRequest();

        // Act
        var result = await service.SaveAsync("TW1234", idempotencyKey, request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsNewRecord);
        Assert.Equal(99, result.Value.Log.LogId);
        Assert.NotNull(result.Value.Log);

        // 確認 InsertAsync 被呼叫恰好一次
        mockRepo.Verify(r => r.InsertAsync(It.IsAny<WarrantTrialLog>()), Times.Once);
    }

    // ── 5. Race Condition（SqlException 2627）→ 查回已存記錄，Success, IsNewRecord=false ──

    [Fact]
    public async Task SaveAsync_UniqueConstraintViolation_ReturnsExistingRecord()
    {
        // Arrange
        var idempotencyKey = Guid.NewGuid();
        var committedLog   = BuildExistingLog(idempotencyKey);
        var sqlEx          = SqlExceptionFactory.Create(2627); // Unique constraint violation

        var mockRepo = new Mock<ITrialLogRepository>();

        // 第一次查詢：key 不存在（模擬兩個並發請求都通過了冪等檢查）
        mockRepo
            .SetupSequence(r => r.FindByIdempotencyKeyAsync(idempotencyKey))
            .ReturnsAsync((WarrantTrialLog?)null) // 第一次呼叫：key 不存在
            .ReturnsAsync(committedLog);           // 第二次呼叫（race condition recovery）：已存記錄

        // InsertAsync 拋出唯一約束例外，模擬 race condition
        mockRepo
            .Setup(r => r.InsertAsync(It.IsAny<WarrantTrialLog>()))
            .ThrowsAsync(sqlEx);

        var service = CreateService(mockRepo.Object);
        var request = BuildValidRequest();

        // Act
        var result = await service.SaveAsync("TW1234", idempotencyKey, request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsNewRecord);           // 非新記錄，是已被並發寫入的記錄
        Assert.Equal(committedLog.LogId, result.Value.Log.LogId);

        // 確認進行了 recovery 查詢（共呼叫兩次 FindByIdempotencyKeyAsync）
        mockRepo.Verify(
            r => r.FindByIdempotencyKeyAsync(idempotencyKey),
            Times.Exactly(2));
    }

    // ── 6. Race Condition（SqlException 2601 重複索引）→ 同樣走 recovery 路徑 ─

    [Fact]
    public async Task SaveAsync_DuplicateIndexViolation_ReturnsExistingRecord()
    {
        var idempotencyKey = Guid.NewGuid();
        var committedLog   = BuildExistingLog(idempotencyKey);
        var sqlEx          = SqlExceptionFactory.Create(2601);

        var mockRepo = new Mock<ITrialLogRepository>();
        mockRepo
            .SetupSequence(r => r.FindByIdempotencyKeyAsync(idempotencyKey))
            .ReturnsAsync((WarrantTrialLog?)null)
            .ReturnsAsync(committedLog);
        mockRepo
            .Setup(r => r.InsertAsync(It.IsAny<WarrantTrialLog>()))
            .ThrowsAsync(sqlEx);

        var service = CreateService(mockRepo.Object);
        var result  = await service.SaveAsync("TW1234", idempotencyKey, BuildValidRequest());

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsNewRecord);
        Assert.Equal(committedLog.LogId, result.Value.Log.LogId);
    }
}
