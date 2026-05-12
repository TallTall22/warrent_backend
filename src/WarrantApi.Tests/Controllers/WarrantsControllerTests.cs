using Microsoft.AspNetCore.Mvc;
using Moq;
using WarrantApi.Common;
using WarrantApi.Controllers;
using WarrantApi.Domain.Entities;
using WarrantApi.DTOs;
using WarrantApi.Services.Interfaces;

namespace WarrantApi.Tests.Controllers;

/// <summary>
/// WarrantsController 單元測試。
/// 涵蓋：分頁清單、單筆查詢 404、試算成功、試算失敗（防禦性檢查）。
/// </summary>
public sealed class WarrantsControllerTests
{
    // ── Test fixture helpers ───────────────────────────────────────────────────

    private static WarrantsController CreateController(IWarrantService service)
        => new(service);

    private static WarrantMaster BuildWarrant() => new()
    {
        WarrantId       = "TW1234",
        WarrantType     = "CALL",
        StrikePrice     = 100m,
        ConversionRatio = 0.1m,
        PositionQty     = 5000
    };

    private static PagedResult<WarrantDto> BuildPagedResult(int total = 1) => new()
    {
        Data = new List<WarrantDto>
        {
            new() { WarrantId = "TW1234", WarrantType = "CALL",
                    StrikePrice = 100m, ConversionRatio = 0.1m, PositionQty = 5000 }
        },
        Total    = total,
        Page     = 1,
        PageSize = 50
    };

    // ── 1. GET /api/warrants → 200 OK，含分頁結構 ─────────────────────────────

    [Fact]
    public async Task GetList_ReturnsOkWithPagedResult()
    {
        var mockService = new Mock<IWarrantService>();
        mockService
            .Setup(s => s.GetWarrantListAsync(null, 1, 50))
            .ReturnsAsync(BuildPagedResult());

        var controller = CreateController(mockService.Object);
        var result     = await controller.GetList(null, 1, 50);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, ok.StatusCode);
        var paged = Assert.IsType<PagedResult<WarrantDto>>(ok.Value);
        Assert.Equal(1, paged.Total);
        Assert.Single(paged.Data);
    }

    // ── 2. GET /api/warrants?keyword=TW1 → 依關鍵字搜尋，回傳 200 ────────────

    [Fact]
    public async Task GetList_WithKeyword_PassesKeywordToService()
    {
        var mockService = new Mock<IWarrantService>();
        mockService
            .Setup(s => s.GetWarrantListAsync("TW1", 1, 50))
            .ReturnsAsync(BuildPagedResult());

        var controller = CreateController(mockService.Object);
        var result     = await controller.GetList("TW1", 1, 50);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, ok.StatusCode);
        mockService.Verify(s => s.GetWarrantListAsync("TW1", 1, 50), Times.Once);
    }

    // ── 3. GET /api/warrants/{id} 存在 → 200 OK，含正確欄位 ──────────────────

    [Fact]
    public async Task GetById_ExistingWarrant_ReturnsOkWithDto()
    {
        var warrant     = BuildWarrant();
        var mockService = new Mock<IWarrantService>();
        mockService
            .Setup(s => s.GetWarrantByIdAsync("TW1234"))
            .ReturnsAsync(warrant);

        var controller = CreateController(mockService.Object);
        var result     = await controller.GetById("TW1234");

        var ok  = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<WarrantDto>(ok.Value);
        Assert.Equal("TW1234", dto.WarrantId);
        Assert.Equal("CALL",   dto.WarrantType);
        Assert.Equal(100m,     dto.StrikePrice);
        Assert.Equal(0.1m,     dto.ConversionRatio);
        Assert.Equal(5000,     dto.PositionQty);
    }

    // ── 4. GET /api/warrants/{id} 不存在 → 404 Not Found ─────────────────────

    [Fact]
    public async Task GetById_NotFound_Returns404()
    {
        var mockService = new Mock<IWarrantService>();
        mockService
            .Setup(s => s.GetWarrantByIdAsync("NOTEXIST"))
            .ReturnsAsync((WarrantMaster?)null);

        var controller = CreateController(mockService.Object);
        var result     = await controller.GetById("NOTEXIST");

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal(404, notFound.StatusCode);
    }

    // ── 5. POST /api/warrants/{id}/calculate 成功 → 200 OK，含完整試算結果 ────

    [Fact]
    public async Task Calculate_Success_ReturnsOkWithTrialCalculationDto()
    {
        var calcDto = new TrialCalculationDto
        {
            WarrantId       = "TW1234",
            MarketPrice     = 105m,
            StrikePrice     = 100m,
            ConversionRatio = 0.1m,
            WarrantType     = "CALL",
            PositionQty     = 5000,
            Delta           = 0.8m,
            DeltaStatus     = "ITM",
            TheoryPrice     = 0.5m,
            HedgeQty        = 400m
        };

        var mockService = new Mock<IWarrantService>();
        mockService
            .Setup(s => s.CalculateAsync("TW1234", 105m))
            .ReturnsAsync(Result<TrialCalculationDto>.Success(calcDto));

        var controller = CreateController(mockService.Object);
        var result     = await controller.Calculate("TW1234", new CalculateRequest { MarketPrice = 105m });

        var ok  = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<TrialCalculationDto>(ok.Value);
        Assert.Equal(0.8m,  dto.Delta);
        Assert.Equal("ITM", dto.DeltaStatus);
        Assert.Equal(0.5m,  dto.TheoryPrice);
        Assert.Equal(400m,  dto.HedgeQty);
    }

    // ── 6. POST /api/warrants/{id}/calculate 非法價格 → 400 Bad Request ────────

    [Fact]
    public async Task Calculate_ServiceFailure_Returns400()
    {
        var mockService = new Mock<IWarrantService>();
        mockService
            .Setup(s => s.CalculateAsync("TW1234", 0m))
            .ReturnsAsync(Result<TrialCalculationDto>.Failure("標的價格必須大於零"));

        var controller = CreateController(mockService.Object);
        var result     = await controller.Calculate("TW1234", new CalculateRequest { MarketPrice = 0m });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, badRequest.StatusCode);
    }

    // ── 7. POST /api/warrants/{id}/calculate 找不到權證 → 400 Bad Request ──────

    [Fact]
    public async Task Calculate_WarrantNotFound_Returns400()
    {
        var mockService = new Mock<IWarrantService>();
        mockService
            .Setup(s => s.CalculateAsync("NOTEXIST", 105m))
            .ReturnsAsync(Result<TrialCalculationDto>.Failure("找不到指定的權證"));

        var controller = CreateController(mockService.Object);
        var result     = await controller.Calculate("NOTEXIST", new CalculateRequest { MarketPrice = 105m });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, badRequest.StatusCode);
    }

    // ── 8. GET warrantId 超過 10 碼 → 400 Bad Request ────────────────────────

    [Fact]
    public async Task GetById_WarrantIdTooLong_Returns400()
    {
        var mockService = new Mock<IWarrantService>(MockBehavior.Strict);
        var controller  = CreateController(mockService.Object);

        var result = await controller.GetById("12345678901"); // 11 碼

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, badRequest.StatusCode);
        mockService.VerifyNoOtherCalls();
    }

    // ── 9. POST calculate warrantId 超過 10 碼 → 400 Bad Request ─────────────

    [Fact]
    public async Task Calculate_WarrantIdTooLong_Returns400()
    {
        var mockService = new Mock<IWarrantService>(MockBehavior.Strict);
        var controller  = CreateController(mockService.Object);

        var result = await controller.Calculate("12345678901", new CalculateRequest { MarketPrice = 100m });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, badRequest.StatusCode);
        mockService.VerifyNoOtherCalls();
    }
}
