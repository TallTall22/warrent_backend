using WarrantApi.Infrastructure;
using WarrantApi.Middleware;
using WarrantApi.Repositories;
using WarrantApi.Repositories.Interfaces;
using WarrantApi.Services;
using WarrantApi.Services.Interfaces;

var builder = WebApplication.CreateBuilder(args);

// ── Controllers ──────────────────────────────────────────────────────────────
builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState
                .Where(e => e.Value?.Errors.Count > 0)
                .SelectMany(e => e.Value!.Errors)
                .Select(e => e.ErrorMessage)
                .ToList();
            return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(new
            {
                success = false,
                message = string.Join("; ", errors)
            });
        };
    });

// ── Swagger / OpenAPI ─────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo
    {
        Title = "Warrant Risk Monitor API",
        Version = "v1"
    });
});

// ── CORS ──────────────────────────────────────────────────────────────────────
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [];

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendPolicy", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// ── Infrastructure ────────────────────────────────────────────────────────────
builder.Services.AddSingleton<AppDbContext>();

// ── Repository 層 ─────────────────────────────────────────────────────────────
builder.Services.AddScoped<IWarrantRepository, WarrantRepository>();
builder.Services.AddScoped<ITrialLogRepository, TrialLogRepository>();

// ── Service 層 ────────────────────────────────────────────────────────────────
builder.Services.AddScoped<IWarrantService, WarrantService>();
builder.Services.AddScoped<ITrialLogService, TrialLogService>();

// ── Application ───────────────────────────────────────────────────────────────
var app = builder.Build();

// 全域例外處理 Middleware 必須在所有其他 middleware 之前註冊
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Warrant API v1");
        options.RoutePrefix = string.Empty; // Swagger UI at root
    });
}

app.UseCors("FrontendPolicy");

app.UseAuthorization();

app.MapControllers();

app.Run();
