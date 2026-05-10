using Microsoft.AspNetCore.Mvc;
using WarrantApi.DTOs;
using WarrantApi.Services.Interfaces;

namespace WarrantApi.Controllers;

/// <summary>
/// 避險試算記錄 API Controller。
/// 僅負責 HTTP 輸入/輸出，不含任何業務邏輯。
/// </summary>
[ApiController]
[Route("api/warrants/{warrantId}/trial-logs")]
public sealed class TrialLogsController : ControllerBase
{
    private readonly ITrialLogService _trialLogService;

    public TrialLogsController(ITrialLogService trialLogService)
    {
        _trialLogService = trialLogService;
    }

    /// <summary>
    /// 儲存避險試算記錄，支援冪等保護。
    /// POST /api/warrants/{warrantId}/trial-logs
    /// Header: X-Idempotency-Key: {UUID}
    /// 成功新寫入 → 201；冪等重複 → 200。
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(TrialLogDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(TrialLogDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Save(
        [FromRoute] string warrantId,
        [FromHeader(Name = "X-Idempotency-Key")] string? idempotencyKeyRaw,
        [FromBody] SaveTrialLogRequest request)
    {
        // Validate presence and format of idempotency key
        if (string.IsNullOrWhiteSpace(idempotencyKeyRaw))
            return BadRequest(new { success = false, message = "缺少必要的 Header：X-Idempotency-Key" });

        if (!Guid.TryParse(idempotencyKeyRaw, out var idempotencyKey))
            return BadRequest(new { success = false, message = "X-Idempotency-Key 格式不正確，必須為合法的 UUID" });

        // Pre-check idempotency: if the key already exists, the service will return
        // the existing record. We need to know this to choose between 200 and 201.
        // The service exposes a CheckIdempotencyAsync for this purpose.
        var existsBefore = await _trialLogService.IdempotencyKeyExistsAsync(idempotencyKey);

        var result = await _trialLogService.SaveAsync(warrantId, idempotencyKey, request);

        if (result.IsFailure)
            return BadRequest(new { success = false, message = result.ErrorMessage });

        // Return 200 for idempotent repeat, 201 for new insert
        return existsBefore
            ? Ok(result.Value)
            : Created(string.Empty, result.Value);
    }

    /// <summary>
    /// 取得指定權證的最近 10 筆試算記錄。
    /// GET /api/warrants/{warrantId}/trial-logs
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRecent([FromRoute] string warrantId)
    {
        var logs = await _trialLogService.GetRecentLogsAsync(warrantId);
        return Ok(new
        {
            warrantId,
            logs
        });
    }
}
