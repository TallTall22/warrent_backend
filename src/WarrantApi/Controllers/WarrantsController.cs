using Microsoft.AspNetCore.Mvc;
using WarrantApi.DTOs;
using WarrantApi.Services.Interfaces;

namespace WarrantApi.Controllers;

/// <summary>
/// 權證主檔 API Controller。
/// 僅負責 HTTP 輸入/輸出，不含任何業務邏輯。
/// </summary>
[ApiController]
[Route("api/warrants")]
public sealed class WarrantsController : ControllerBase
{
    private readonly IWarrantService _warrantService;

    public WarrantsController(IWarrantService warrantService)
    {
        _warrantService = warrantService;
    }

    /// <summary>
    /// 取得權證分頁清單，支援關鍵字前綴搜尋。
    /// GET /api/warrants?keyword=03&page=1&pageSize=50
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<WarrantDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetList(
        [FromQuery] string? keyword,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        if (page < 1)     page     = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 200) pageSize = 200;

        var result = await _warrantService.GetWarrantListAsync(keyword, page, pageSize);
        return Ok(result);
    }

    /// <summary>
    /// 依權證代號取得單筆權證主檔。
    /// GET /api/warrants/{warrantId}
    /// </summary>
    [HttpGet("{warrantId}")]
    [ProducesResponseType(typeof(WarrantDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] string warrantId)
    {
        var warrant = await _warrantService.GetWarrantByIdAsync(warrantId);

        if (warrant is null)
            return NotFound(new { success = false, message = $"找不到權證代號 '{warrantId}'" });

        return Ok(new WarrantDto
        {
            WarrantId       = warrant.WarrantId,
            StrikePrice     = warrant.StrikePrice,
            ConversionRatio = warrant.ConversionRatio,
            WarrantType     = warrant.WarrantType,
            PositionQty     = warrant.PositionQty
        });
    }

    /// <summary>
    /// 執行避險試算計算（純計算，不寫入資料庫）。
    /// POST /api/warrants/{warrantId}/calculate
    /// Body: { "marketPrice": 105.00 }
    /// </summary>
    [HttpPost("{warrantId}/calculate")]
    [ProducesResponseType(typeof(TrialCalculationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Calculate(
        [FromRoute] string warrantId,
        [FromBody] CalculateRequest request)
    {
        var result = await _warrantService.CalculateAsync(warrantId, request.MarketPrice);

        if (result.IsFailure)
            return BadRequest(new { success = false, message = result.ErrorMessage });

        return Ok(result.Value);
    }
}
