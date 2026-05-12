using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WarrantApi.Domain.Entities;
using WarrantApi.Repositories.Interfaces;
using WarrantApi.Services;

namespace WarrantApi.Tests.Services;

/// <summary>
/// WarrantService.CalculateAsync 單元測試。
/// 涵蓋：輸入驗證、權證不存在、CALL/PUT 各 Delta 狀態、理論價值下限、避險數量計算。
/// </summary>
public sealed class WarrantServiceTests
{
    // ── Test fixture helpers ───────────────────────────────────────────────────

    private static WarrantService CreateService(IWarrantRepository repo)
        => new(repo, NullLogger<WarrantService>.Instance);

    /// <summary>
    /// 建立一個標準的 CALL 權證主檔，用於各計算情境測試。
    /// </summary>
    private static WarrantMaster BuildCallWarrant(
        decimal strikePrice     = 100m,
        decimal conversionRatio = 0.1m,
        int     positionQty     = 5000)
        => new()
        {
            WarrantId       = "TW1234",
            WarrantType     = "CALL",
            StrikePrice     = strikePrice,
            ConversionRatio = conversionRatio,
            PositionQty     = positionQty
        };

    /// <summary>
    /// 建立一個標準的 PUT 權證主檔。
    /// </summary>
    private static WarrantMaster BuildPutWarrant(
        decimal strikePrice     = 100m,
        decimal conversionRatio = 0.1m,
        int     positionQty     = 5000)
        => new()
        {
            WarrantId       = "TW5678",
            WarrantType     = "PUT",
            StrikePrice     = strikePrice,
            ConversionRatio = conversionRatio,
            PositionQty     = positionQty
        };

    // ── 1. 非法市場價格 → Failure，不呼叫 Repository ─────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100.5)]
    public async Task CalculateAsync_InvalidMarketPrice_ReturnsFailure(decimal marketPrice)
    {
        // Arrange
        var mockRepo = new Mock<IWarrantRepository>(MockBehavior.Strict);
        // MockBehavior.Strict 確保若呼叫任何 Repository 方法則測試失敗
        var service = CreateService(mockRepo.Object);

        // Act
        var result = await service.CalculateAsync("TW1234", marketPrice);

        // Assert
        Assert.True(result.IsFailure);
        Assert.False(result.IsSuccess);
        Assert.Contains("大於零", result.ErrorMessage);
    }

    // ── 2. warrantId 不存在 → Failure ─────────────────────────────────────────

    [Fact]
    public async Task CalculateAsync_WarrantNotFound_ReturnsFailure()
    {
        // Arrange
        var mockRepo = new Mock<IWarrantRepository>();
        mockRepo
            .Setup(r => r.GetByIdAsync("NOTEXIST"))
            .ReturnsAsync((WarrantMaster?)null);

        var service = CreateService(mockRepo.Object);

        // Act
        var result = await service.CalculateAsync("NOTEXIST", 100m);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("找不到", result.ErrorMessage);
    }

    // ── 3. CALL ITM：marketPrice > strikePrice → Delta=0.8, DeltaStatus="ITM" ─

    [Fact]
    public async Task CalculateAsync_CallITM_ReturnsCorrectDelta()
    {
        // Arrange: CALL, marketPrice=105 > strikePrice=100
        var warrant = BuildCallWarrant(strikePrice: 100m);
        var mockRepo = new Mock<IWarrantRepository>();
        mockRepo.Setup(r => r.GetByIdAsync(warrant.WarrantId)).ReturnsAsync(warrant);
        var service = CreateService(mockRepo.Object);

        // Act
        var result = await service.CalculateAsync(warrant.WarrantId, 105m);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(0.8m, result.Value!.Delta);
        Assert.Equal("ITM", result.Value.DeltaStatus);
    }

    // ── 4. CALL ATM：marketPrice == strikePrice → Delta=0.5, DeltaStatus="ATM" ─

    [Fact]
    public async Task CalculateAsync_CallATM_ReturnsCorrectDelta()
    {
        // Arrange: CALL, marketPrice=100 == strikePrice=100
        var warrant = BuildCallWarrant(strikePrice: 100m);
        var mockRepo = new Mock<IWarrantRepository>();
        mockRepo.Setup(r => r.GetByIdAsync(warrant.WarrantId)).ReturnsAsync(warrant);
        var service = CreateService(mockRepo.Object);

        // Act
        var result = await service.CalculateAsync(warrant.WarrantId, 100m);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(0.5m, result.Value!.Delta);
        Assert.Equal("ATM", result.Value.DeltaStatus);
    }

    // ── 5. CALL OTM：marketPrice < strikePrice → Delta=0.2, DeltaStatus="OTM" ─

    [Fact]
    public async Task CalculateAsync_CallOTM_ReturnsCorrectDelta()
    {
        // Arrange: CALL, marketPrice=95 < strikePrice=100
        var warrant = BuildCallWarrant(strikePrice: 100m);
        var mockRepo = new Mock<IWarrantRepository>();
        mockRepo.Setup(r => r.GetByIdAsync(warrant.WarrantId)).ReturnsAsync(warrant);
        var service = CreateService(mockRepo.Object);

        // Act
        var result = await service.CalculateAsync(warrant.WarrantId, 95m);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(0.2m, result.Value!.Delta);
        Assert.Equal("OTM", result.Value.DeltaStatus);
    }

    // ── 6. PUT ITM：marketPrice < strikePrice → Delta=0.8, DeltaStatus="ITM" ──

    [Fact]
    public async Task CalculateAsync_PutITM_ReturnsCorrectDelta()
    {
        // Arrange: PUT, marketPrice=95 < strikePrice=100 → ITM for PUT
        var warrant = BuildPutWarrant(strikePrice: 100m);
        var mockRepo = new Mock<IWarrantRepository>();
        mockRepo.Setup(r => r.GetByIdAsync(warrant.WarrantId)).ReturnsAsync(warrant);
        var service = CreateService(mockRepo.Object);

        // Act
        var result = await service.CalculateAsync(warrant.WarrantId, 95m);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(0.8m, result.Value!.Delta);
        Assert.Equal("ITM", result.Value.DeltaStatus);
    }

    // ── 7. PUT OTM：marketPrice > strikePrice → Delta=0.2, DeltaStatus="OTM" ──

    [Fact]
    public async Task CalculateAsync_PutOTM_ReturnsCorrectDelta()
    {
        // Arrange: PUT, marketPrice=105 > strikePrice=100 → OTM for PUT
        var warrant = BuildPutWarrant(strikePrice: 100m);
        var mockRepo = new Mock<IWarrantRepository>();
        mockRepo.Setup(r => r.GetByIdAsync(warrant.WarrantId)).ReturnsAsync(warrant);
        var service = CreateService(mockRepo.Object);

        // Act
        var result = await service.CalculateAsync(warrant.WarrantId, 105m);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(0.2m, result.Value!.Delta);
        Assert.Equal("OTM", result.Value.DeltaStatus);
    }

    // ── 8. CALL OTM → TheoryPrice=0（Max(0, 負數) = 0）────────────────────────

    [Fact]
    public async Task CalculateAsync_CallOTM_TheoryPriceIsZero()
    {
        // Arrange: CALL OTM, marketPrice=95, strikePrice=100
        // intrinsicValue = 95 - 100 = -5, theoryPrice = Max(0, -5 * 0.1) = 0
        var warrant = BuildCallWarrant(strikePrice: 100m, conversionRatio: 0.1m);
        var mockRepo = new Mock<IWarrantRepository>();
        mockRepo.Setup(r => r.GetByIdAsync(warrant.WarrantId)).ReturnsAsync(warrant);
        var service = CreateService(mockRepo.Object);

        // Act
        var result = await service.CalculateAsync(warrant.WarrantId, 95m);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(0m, result.Value!.TheoryPrice);
    }

    // ── 9. PUT ATM：marketPrice == strikePrice → Delta=0.5 ───────────────────

    [Fact]
    public async Task CalculateAsync_PutATM_ReturnsCorrectDelta()
    {
        var warrant = BuildPutWarrant(strikePrice: 100m);
        var mockRepo = new Mock<IWarrantRepository>();
        mockRepo.Setup(r => r.GetByIdAsync(warrant.WarrantId)).ReturnsAsync(warrant);
        var service = CreateService(mockRepo.Object);

        var result = await service.CalculateAsync(warrant.WarrantId, 100m);

        Assert.True(result.IsSuccess);
        Assert.Equal(0.5m, result.Value!.Delta);
        Assert.Equal("ATM", result.Value.DeltaStatus);
    }

    // ── 10. PUT ITM：理論價值 = Max(0, (履約價 - 市價) * 行使比例) ──────────

    [Fact]
    public async Task CalculateAsync_PutITM_TheoryPriceIsCorrect()
    {
        // strikePrice=100, marketPrice=90, conversionRatio=0.1
        // intrinsicValue = 100 - 90 = 10, theoryPrice = 10 * 0.1 = 1.0
        var warrant = BuildPutWarrant(strikePrice: 100m, conversionRatio: 0.1m);
        var mockRepo = new Mock<IWarrantRepository>();
        mockRepo.Setup(r => r.GetByIdAsync(warrant.WarrantId)).ReturnsAsync(warrant);
        var service = CreateService(mockRepo.Object);

        var result = await service.CalculateAsync(warrant.WarrantId, 90m);

        Assert.True(result.IsSuccess);
        Assert.Equal(1.0m, result.Value!.TheoryPrice);
        Assert.Equal("ITM", result.Value.DeltaStatus);
    }

    // ── 11. PUT OTM：理論價值 = 0（Max(0, 負數) = 0）────────────────────────

    [Fact]
    public async Task CalculateAsync_PutOTM_TheoryPriceIsZero()
    {
        // strikePrice=100, marketPrice=110 → PUT OTM → intrinsicValue = 100-110 = -10 → 0
        var warrant = BuildPutWarrant(strikePrice: 100m, conversionRatio: 0.1m);
        var mockRepo = new Mock<IWarrantRepository>();
        mockRepo.Setup(r => r.GetByIdAsync(warrant.WarrantId)).ReturnsAsync(warrant);
        var service = CreateService(mockRepo.Object);

        var result = await service.CalculateAsync(warrant.WarrantId, 110m);

        Assert.True(result.IsSuccess);
        Assert.Equal(0m, result.Value!.TheoryPrice);
        Assert.Equal("OTM", result.Value.DeltaStatus);
    }

    // ── 12. HedgeQty 計算正確：positionQty * conversionRatio * delta ───────────

    [Fact]
    public async Task CalculateAsync_HedgeQtyCalculation_IsCorrect()
    {
        // Arrange: CALL ITM
        // positionQty=5000, conversionRatio=0.1, delta=0.8(ITM)
        // Expected hedgeQty = 5000 * 0.1 * 0.8 = 400
        // Expected theoryPrice = (105 - 100) * 0.1 = 0.5
        var warrant = BuildCallWarrant(
            strikePrice:     100m,
            conversionRatio: 0.1m,
            positionQty:     5000);
        var mockRepo = new Mock<IWarrantRepository>();
        mockRepo.Setup(r => r.GetByIdAsync(warrant.WarrantId)).ReturnsAsync(warrant);
        var service = CreateService(mockRepo.Object);

        // Act
        var result = await service.CalculateAsync(warrant.WarrantId, 105m);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(400m, result.Value!.HedgeQty);
        Assert.Equal(0.5m, result.Value.TheoryPrice);
        Assert.Equal(0.8m, result.Value.Delta);
        Assert.Equal("ITM", result.Value.DeltaStatus);
    }
}
