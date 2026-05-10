using System.Net;
using System.Text.Json;

namespace WarrantApi.Middleware;

/// <summary>
/// 全域例外處理 Middleware。
/// 攔截所有未處理的例外，回傳標準化 500 錯誤回應，並記錄完整 exception 資訊。
/// 必須在 pipeline 最前面註冊，以確保能攔截所有後續 middleware 與 controller 的例外。
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    // Reusable JSON serializer options to avoid repeated allocations
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next   = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "未處理的例外 Path={Path} Method={Method}",
                context.Request.Path,
                context.Request.Method);

            await WriteErrorResponseAsync(context);
        }
    }

    private static async Task WriteErrorResponseAsync(HttpContext context)
    {
        context.Response.StatusCode  = (int)HttpStatusCode.InternalServerError;
        context.Response.ContentType = "application/json; charset=utf-8";

        var body = JsonSerializer.Serialize(
            new { success = false, message = "伺服器發生錯誤，請稍後再試" },
            JsonOptions);

        await context.Response.WriteAsync(body);
    }
}
