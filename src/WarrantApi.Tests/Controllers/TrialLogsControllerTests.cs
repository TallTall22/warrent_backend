using Microsoft.AspNetCore.Mvc;
using Moq;
using WarrantApi.Common;
using WarrantApi.Controllers;
using WarrantApi.DTOs;
using WarrantApi.Services.Interfaces;

namespace WarrantApi.Tests.Controllers;

/// <summary>
/// TrialLogsController.Save 單元測試。
/// 驗證 HTTP 語意：400 Bad Request / 201 Created / 200 OK 的正確對應。
/// </summary>
public sealed class TrialLogsControllerTests
{
    // ── Test fixture helpers ───────────────────────────────────────────────────

    private static TrialLogsController CreateController(ITrialLogService service)
        => new(service);

    private static SaveTrialLogRequest BuildValidRequest() => new()
    {
        MarketPrice = 105m,
        TheoryPrice = 0.5m,
        HedgeQty    = 400m
    };

    private static TrialLogDto BuildTrialLogDto() => new()
    {
        LogId       = 1,
        WarrantId   = "TW1234",
        MarketPrice = 105m,
        TheoryPrice = 0.5m,
        HedgeQty    = 400m,
        CreatedTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    // ── 1. 缺少 X-Idempotency-Key → 400 Bad Request ───────────────────────────

    [Fact]
    public async Task Save_MissingIdempotencyKey_Returns400()
    {
        // Arrange
        var mockService  = new Mock<ITrialLogService>(MockBehavior.Strict);
        var controller   = CreateController(mockService.Object);
        var request      = BuildValidRequest();

        // Act
        var actionResult = await controller.Save("TW1234", null, request);

        // Assert
        var badRequest = Assert.IsType<BadRequestObjectResult>(actionResult);
        Assert.Equal(400, badRequest.StatusCode);
        mockService.VerifyNoOtherCalls(); // Service 不應被呼叫
    }

    // ── 2. X-Idempotency-Key 非 UUID 格式 → 400 Bad Request ──────────────────

    [Fact]
    public async Task Save_InvalidIdempotencyKey_Returns400()
    {
        // Arrange
        var mockService  = new Mock<ITrialLogService>(MockBehavior.Strict);
        var controller   = CreateController(mockService.Object);
        var request      = BuildValidRequest();

        // Act
        var actionResult = await controller.Save("TW1234", "not-a-valid-uuid", request);

        // Assert
        var badRequest = Assert.IsType<BadRequestObjectResult>(actionResult);
        Assert.Equal(400, badRequest.StatusCode);
        mockService.VerifyNoOtherCalls();
    }

    // ── 3. Service 回傳 Failure → 400 Bad Request ─────────────────────────────

    [Fact]
    public async Task Save_ServiceFailure_Returns400()
    {
        // Arrange
        var idempotencyKey = Guid.NewGuid();
        var mockService    = new Mock<ITrialLogService>();
        mockService
            .Setup(s => s.SaveAsync("TW1234", idempotencyKey, It.IsAny<SaveTrialLogRequest>()))
            .ReturnsAsync(Result<TrialLogSaveResult>.Failure("標的價格必須大於零，禁止存檔"));

        var controller = CreateController(mockService.Object);
        var request    = BuildValidRequest();

        // Act
        var actionResult = await controller.Save("TW1234", idempotencyKey.ToString(), request);

        // Assert
        var badRequest = Assert.IsType<BadRequestObjectResult>(actionResult);
        Assert.Equal(400, badRequest.StatusCode);
    }

    // ── 4. IsNewRecord=true → 201 Created ────────────────────────────────────

    [Fact]
    public async Task Save_NewRecord_Returns201()
    {
        // Arrange
        var idempotencyKey = Guid.NewGuid();
        var logDto         = BuildTrialLogDto();
        var saveResult     = new TrialLogSaveResult { Log = logDto, IsNewRecord = true };

        var mockService = new Mock<ITrialLogService>();
        mockService
            .Setup(s => s.SaveAsync("TW1234", idempotencyKey, It.IsAny<SaveTrialLogRequest>()))
            .ReturnsAsync(Result<TrialLogSaveResult>.Success(saveResult));

        var controller = CreateController(mockService.Object);
        var request    = BuildValidRequest();

        // Act
        var actionResult = await controller.Save("TW1234", idempotencyKey.ToString(), request);

        // Assert
        var createdResult = Assert.IsType<CreatedResult>(actionResult);
        Assert.Equal(201, createdResult.StatusCode);

        var returnedLog = Assert.IsType<TrialLogDto>(createdResult.Value);
        Assert.Equal(logDto.LogId, returnedLog.LogId);
    }

    // ── 5. IsNewRecord=false → 200 OK ────────────────────────────────────────

    [Fact]
    public async Task Save_DuplicateRecord_Returns200()
    {
        // Arrange
        var idempotencyKey = Guid.NewGuid();
        var logDto         = BuildTrialLogDto();
        var saveResult     = new TrialLogSaveResult { Log = logDto, IsNewRecord = false };

        var mockService = new Mock<ITrialLogService>();
        mockService
            .Setup(s => s.SaveAsync("TW1234", idempotencyKey, It.IsAny<SaveTrialLogRequest>()))
            .ReturnsAsync(Result<TrialLogSaveResult>.Success(saveResult));

        var controller = CreateController(mockService.Object);
        var request    = BuildValidRequest();

        // Act
        var actionResult = await controller.Save("TW1234", idempotencyKey.ToString(), request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        Assert.Equal(200, okResult.StatusCode);

        var returnedLog = Assert.IsType<TrialLogDto>(okResult.Value);
        Assert.Equal(logDto.LogId, returnedLog.LogId);
    }
}
