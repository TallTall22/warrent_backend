# 後端 Code Review 報告

**審查日期**：2026-05-10
**審查人**：資深後端工程師（10 年以上 C# .NET Core + SQL Server + 金融系統）
**範圍**：`src/WarrantApi/` 全部原始碼 + `Database/` SQL Scripts
**建置狀態**：dotnet build 成功（0 錯誤、0 警告）

---

## 執行摘要

**整體評分：4.1 / 5.0**

**主要優點：**
- 分層架構清晰，Controller / Service / Repository 職責邊界嚴格遵守
- 所有金融數值欄位 100% 使用 `decimal`，無 float/double 洩漏
- 冪等性機制完整，包含 Race Condition（SqlException 2627/2601）防護
- Result Pattern 使用一致，無例外驅動的業務流程控制
- SQL 無 N+1 查詢，全部 Set-based；單次往返取 COUNT + 資料頁，效能佳
- 全域例外 Middleware 不洩漏 stack trace，安全性正確
- 結構化 Logging 完整（ILogger<T>，參數化 log message）

**主要風險：**
1. `TrialLogsController.Save` 有雙重查詢 Race Condition 問題（`IdempotencyKeyExistsAsync` 後再 `SaveAsync` 之間的視窗期會導致 201/200 回傳碼誤判）
2. `WarrantRepository.GetListAsync` 對空 keyword 邊界處理依賴 SQL 隱式邏輯，可讀性偏低
3. `CalculateRequest` 的 `[Range]` 使用 `double.MaxValue`，與金融精度 `decimal` 最大值不一致
4. `GetRecent` endpoint 不驗證 warrantId 存在性，回傳空陣列而非 404
5. `pageSize` 上限實作（200）與計畫書規格（100）不一致

---

## 問題清單

---

### [TrialLogsController.cs:47-57] 201/200 判斷存在 TOCTOU Race Condition

**嚴重等級**：🔴 Critical

**問題描述**：
`Save` Action 在呼叫 `_trialLogService.SaveAsync` 之前，先呼叫 `IdempotencyKeyExistsAsync` 確認 key 是否存在，以此決定回傳 201 或 200。這兩次呼叫之間存在時間視窗（Time-of-Check-Time-of-Use, TOCTOU），在高並發場景下：

- 請求 A 查詢 key 不存在（existsBefore = false）
- 請求 B 同時抵達，完成寫入
- 請求 A 的 `SaveAsync` 進入 Race Condition 處理，取回已存結果並回傳 `Result.Success`
- 結果：請求 A 認為「新寫入」（existsBefore = false），回傳 201，但實際上是冪等回傳，語意錯誤

此外，`IdempotencyKeyExistsAsync` 會對資料庫多一次額外查詢，而 `SaveAsync` 內部的冪等邏輯已包含完整的查詢，造成非必要的雙重查詢（在非並發場景下每次 POST 都會觸發 2 次 DB 查詢）。

**建議修正**：
讓 `SaveAsync` 回傳一個帶有「是否為冪等重複」旗標的結果，Controller 直接使用該旗標決定狀態碼，無需在 Controller 層做額外查詢：

```csharp
// 新增 DTO 或使用 enum 區分結果類型
public record SaveTrialLogResult(TrialLogDto Dto, bool WasAlreadyExisting);

// TrialLogService.SaveAsync 回傳時帶旗標
return Result<SaveTrialLogResult>.Success(new SaveTrialLogResult(MapToDto(existing), WasAlreadyExisting: true));

// Controller 直接依旗標決定狀態碼，無需額外查詢
return result.Value.WasAlreadyExisting
    ? Ok(result.Value.Dto)         // 200 — 冪等重複
    : Created(string.Empty, result.Value.Dto); // 201 — 新寫入
```

---

### [WarrantsController.cs:33-35] pageSize 上限與計畫書規格不一致

**嚴重等級**：🟡 Warning

**問題描述**：
`plan.md` 明確規格「pageSize 最大 100」，但 Controller 實作上限為 200：

```csharp
if (pageSize > 200) pageSize = 200;  // 計畫書規格為 100
```

雖然 200 筆在效能上仍可接受，但規格不一致會導致前端開發者誤解 API 行為，且若未來需要縮緊上限，已在生產環境流通的前端程式碼可能依賴 > 100 的 pageSize。

**建議修正**：

```csharp
// 與 plan.md 保持一致，或明確文件化為刻意放寬
private const int MaxPageSize = 100; // 與 plan.md 規格一致
if (pageSize > MaxPageSize) pageSize = MaxPageSize;
```

---

### [CalculateRequest.cs:12 / SaveTrialLogRequest.cs:14] [Range] 使用 double.MaxValue 而非 decimal.MaxValue 語意

**嚴重等級**：🟡 Warning

**問題描述**：
`[Range(0.0001, double.MaxValue)]` 的上限使用了 `double.MaxValue`。雖然 ASP.NET Core 會正確處理 `decimal` 屬性的 `[Range]` 驗證，但 `double.MaxValue`（約 1.8e308）遠超 `decimal.MaxValue`（約 7.9e28），語意不清晰，且在型別轉換時存在隱含風險。更直觀且安全的寫法是明確指定字串格式或使用 `decimal` 常數：

```csharp
// 更精確的表達方式（避免 double/decimal 混用語意）
[Range(typeof(decimal), "0.0001", "79228162514264337593543950335",
    ErrorMessage = "標的價格必須大於零")]
public decimal MarketPrice { get; set; }
```

---

### [TrialLogsController.cs:66-74] GET trial-logs 不驗證 warrantId 存在性

**嚴重等級**：🟡 Warning

**問題描述**：
`GetRecent` Action 對任意 `warrantId`（包含不存在的代號）都直接回傳 200 空陣列，不驗證權證是否存在。這在語意上不正確：若 warrantId 不存在，應回傳 404 而非空的成功回應。前端若依賴空陣列來判斷「無記錄」，將無法區分「有效權證但無試算記錄」和「無效的 warrantId」兩種情況。

**建議修正**：

```csharp
[HttpGet]
public async Task<IActionResult> GetRecent([FromRoute] string warrantId)
{
    var warrant = await _warrantService.GetWarrantByIdAsync(warrantId);
    if (warrant is null)
        return NotFound(new { success = false, message = $"找不到權證代號 '{warrantId}'" });

    var logs = await _trialLogService.GetRecentLogsAsync(warrantId);
    return Ok(new { warrantId, logs });
}
```

注意：若採用此修正，`TrialLogsController` 需注入 `IWarrantService`。

---

### [WarrantRepository.cs:26-53] conn.Open() 為同步呼叫

**嚴重等級**：🟡 Warning

**問題描述**：
`GetListAsync`、`GetByIdAsync` 以及所有 Repository 方法中，`conn.Open()` 是同步呼叫，而整個方法被宣告為 `async`。在高並發場景下，同步 Open() 會阻塞執行緒，降低 ASP.NET Core 的 I/O 效率優勢。

```csharp
using var conn = _db.CreateConnection();
conn.Open();  // 同步，阻塞執行緒
```

**建議修正**：

```csharp
using var conn = _db.CreateConnection();
await conn.OpenAsync();  // 非同步，不阻塞執行緒池
```

`IDbConnection` 實際為 `SqlConnection`（實作 `DbConnection`），支援 `OpenAsync()`。需將 `IDbConnection` 轉型或在 `AppDbContext.CreateConnection()` 回傳 `DbConnection`（或 `SqlConnection`）。

---

### [TrialLogRepository.cs:57] 交易保護範圍冗餘

**嚴重等級**：🟢 Suggestion

**問題描述**：
`InsertAsync` 使用 `BeginTransaction()` 包裹「INSERT + SCOPE_IDENTITY()」，此做法本身正確，但 SQL 批次：

```sql
INSERT INTO Warrant_Trial_Log (...) VALUES (...);
SELECT CAST(SCOPE_IDENTITY() AS INT);
```

在 SQL Server 中，`SCOPE_IDENTITY()` 必須在同一個 scope（同一批次/預存程序）執行才能正確取值，Dapper 的 `ExecuteScalarAsync` 在同一次網路往返執行這兩個陳述式，已天然保證一致性，不需外層的 `BeginTransaction` 額外包裹。這個 Transaction 在沒有其他並發操作的情況下是多餘的，且增加了 lock 競爭的機率。

若需要保留冪等性相關的並發保護，更適合的做法是在 Service 層管理 Transaction，而非 Repository 層。

**建議修正**（若僅要取得 LogId）：

```csharp
// 不需要 Transaction，僅利用 Dapper 批次執行保證 SCOPE_IDENTITY() 正確性
using var conn = _db.CreateConnection();
await conn.OpenAsync();
log.LogId = await conn.ExecuteScalarAsync<int>(sql, new { ... });
return log;
```

---

### [WarrantService.cs:41] GetWarrantByIdAsync 回傳 Domain Entity 而非 DTO

**嚴重等級**：🟢 Suggestion

**問題描述**：
`IWarrantService.GetWarrantByIdAsync` 回傳 `WarrantMaster`（Domain Entity），而非 `WarrantDto`。這使得 Controller 需要手動執行 mapping（`WarrantsController.cs` 第 55-62 行），違反了讓 Service 層封裝 mapping 的原則，且 Domain Entity 直接暴露給 Controller 層，降低了分層隔離性。

```csharp
// Controller 中有 mapping 邏輯（應屬 Service 職責）
return Ok(new WarrantDto
{
    WarrantId       = warrant.WarrantId,
    StrikePrice     = warrant.StrikePrice,
    ...
});
```

**建議修正**：

```csharp
// IWarrantService 改為回傳 WarrantDto?
Task<WarrantDto?> GetWarrantByIdAsync(string warrantId);

// Controller 簡化為
var dto = await _warrantService.GetWarrantByIdAsync(warrantId);
if (dto is null) return NotFound(...);
return Ok(dto);
```

---

### [TrialLogRepository.cs:81-102] GetRecentByWarrantIdAsync count 參數可能被任意值傳入

**嚴重等級**：🟢 Suggestion

**問題描述**：
`GetRecentByWarrantIdAsync(string warrantId, int count = 10)` 的 `count` 參數沒有上限驗證。若未來呼叫端傳入極大值（如 100000），將直接翻譯為 `SELECT TOP (100000)`，可能導致效能問題。雖然目前呼叫端（`TrialLogService`）使用預設值 10，但公開的 interface 應防禦性設計。

**建議修正**：

```csharp
private const int MaxRecentCount = 100;

public async Task<IEnumerable<WarrantTrialLog>> GetRecentByWarrantIdAsync(
    string warrantId, int count = 10)
{
    count = Math.Clamp(count, 1, MaxRecentCount);
    // ...
}
```

---

### [SaveTrialLogRequest.cs:17-18] TheoryPrice 允許 0 但 DB CHECK 約束也允許 0，有語意疑慮

**嚴重等級**：🟢 Suggestion

**問題描述**：
`TheoryPrice` 的 `[Range(0, double.MaxValue)]` 允許值為 0，這在業務上是合理的（OTM 的理論價值為 0）。但對照資料庫的 `CK_Theory_Price CHECK (Theory_Price >= 0)` 約束，兩者一致，無問題。此項為確認項，無需修改。

實際的 Suggestion 是：`HedgeQty` 在 ATM 場景（delta = 0.5）時不可能為 0（除非 positionQty 為 0），但前端可能傳入任意計算好的 HedgeQty，後端並不重新驗算。若業務規格要求後端強制驗算，應在 `TrialLogService.SaveAsync` 加入重算驗證。

---

### [Program.cs:52] AppDbContext 使用 Singleton 需注意執行緒安全

**嚴重等級**：🟢 Suggestion

**問題描述**：
`AppDbContext` 以 `AddSingleton` 註冊，而 `AppDbContext` 本身只持有 `_connectionString`（string，不可變），`CreateConnection()` 每次呼叫都建立新的 `SqlConnection`，因此執行緒安全沒有問題。

但若未來有人擴充 `AppDbContext`（如加入快取連線或狀態），可能引入執行緒安全問題。建議在類別上加入 `// Thread-safe: stateless, CreateConnection() always returns a new connection` 的明確注釋，並在 code review checklist 中標記此假設。

---

### [ExceptionHandlingMiddleware.cs:48] Response 已開始輸出時可能拋出例外

**嚴重等級**：🟢 Suggestion

**問題描述**：
在 `WriteErrorResponseAsync` 中，如果 `context.Response.HasStarted` 為 true（即 response headers 已開始傳輸到客戶端），設定 `context.Response.StatusCode` 會拋出 `InvalidOperationException`，導致更嚴重的錯誤。

**建議修正**：

```csharp
private static async Task WriteErrorResponseAsync(HttpContext context)
{
    if (context.Response.HasStarted)
        return; // 無法修改已開始傳輸的 response，靜默放棄

    context.Response.StatusCode  = (int)HttpStatusCode.InternalServerError;
    context.Response.ContentType = "application/json; charset=utf-8";

    var body = JsonSerializer.Serialize(
        new { success = false, message = "伺服器發生錯誤，請稍後再試" },
        JsonOptions);

    await context.Response.WriteAsync(body);
}
```

---

### [WarrantService.cs / TrialLogService.cs] WarrantType Enum 未被使用於業務邏輯

**嚴重等級**：🟢 Suggestion

**問題描述**：
`Domain/Enums/WarrantType.cs` 定義了 `WarrantType` enum（CALL/PUT），但 `WarrantService.CalculateDelta` 和 `CalculateTheoryPrice` 中使用的是 `string.Equals("CALL", ...)` 比較，未使用 Enum。這使得如果 DB 中出現大小寫不一致的值（如 "call"），`StringComparison.OrdinalIgnoreCase` 雖可保護，但整體設計不夠嚴謹。

**建議修正**：
在 `WarrantMaster` entity mapping 時，將 `WarrantType` 字串轉換為 `WarrantType` Enum，後續業務邏輯使用 Enum 比較，避免 magic string：

```csharp
private static (decimal delta, string deltaStatus) CalculateDelta(
    WarrantType warrantType, decimal marketPrice, decimal strikePrice)
{
    if (marketPrice == strikePrice)
        return (0.5m, "ATM");

    bool isITM = warrantType == WarrantType.CALL
        ? marketPrice > strikePrice
        : marketPrice < strikePrice;

    return isITM ? (0.8m, "ITM") : (0.2m, "OTM");
}
```

---

### [TrialLogsController.cs:34] X-Idempotency-Key 為 nullable string 但宣告為 string?

**嚴重等級**：🟢 Suggestion

**問題描述**：
`[FromHeader(Name = "X-Idempotency-Key")] string? idempotencyKeyRaw` 的 nullable 宣告是正確的（Header 可能不存在）。後續立即有 null/whitespace 驗證（第 38-39 行），處理正確。此為正面確認項。

附帶建議：可考慮將 Header 名稱 `"X-Idempotency-Key"` 抽取為常數，避免若有多個 Controller 需要讀取此 Header 時重複硬編碼：

```csharp
private const string IdempotencyKeyHeader = "X-Idempotency-Key";

[FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKeyRaw
```

---

## 整體評分

| 審查面向 | 評分（1-5） | 說明 |
|----------|-------------|------|
| 金融精度 | 5 / 5 | 所有金融欄位 100% 使用 decimal，無任何 float/double 洩漏 |
| 分層架構 | 4 / 5 | Controller/Service/Repository 職責邊界清晰；GetWarrantByIdAsync 回傳 Domain Entity 輕微違反封裝 |
| 錯誤處理 | 4 / 5 | Result Pattern 使用一致；ExceptionHandlingMiddleware 未處理 Response.HasStarted |
| 冪等性實作 | 4 / 5 | 機制完整，含 Race Condition 處理；但 201/200 判斷的 TOCTOU 問題需修正 |
| SQL 品質 | 5 / 5 | 無 N+1，全 Set-based；參數化查詢防 SQL Injection；索引策略完整 |
| API 設計 | 4 / 5 | RESTful 規範；pageSize 上限不一致；GetRecent 缺少 404 防護 |
| 程式碼品質 | 4 / 5 | 命名一致；無 .Result/.Wait()；conn.Open() 為同步呼叫 |
| 安全性 | 5 / 5 | Service 層有第二道驗證；不洩漏 stack trace；參數化 SQL 完整 |

---

## 優先修正清單（依嚴重等級排序）

### 必須修正（Critical）

1. **[TrialLogsController.cs]** 移除 `IdempotencyKeyExistsAsync` 前置查詢，改由 `SaveAsync` 回傳帶旗標的結果，消除 TOCTOU Race Condition 並減少一次 DB 查詢

### 建議修正（Warning）

2. **[WarrantsController.cs]** 將 `pageSize` 上限從 200 改回 100（與 plan.md 規格一致），或明確文件化為刻意放寬
3. **[CalculateRequest.cs / SaveTrialLogRequest.cs]** 將 `[Range]` 上限從 `double.MaxValue` 改為 `decimal` 字串格式，避免型別混用語意問題
4. **[TrialLogsController.cs:GetRecent]** 加入 warrantId 存在性驗證，對不存在的 warrantId 回傳 404
5. **[WarrantRepository.cs / TrialLogRepository.cs]** 將 `conn.Open()` 改為 `await conn.OpenAsync()`，確保完整非同步 I/O

### 可選改善（Suggestion）

6. **[ExceptionHandlingMiddleware.cs]** 加入 `context.Response.HasStarted` 防護
7. **[WarrantService.cs]** `GetWarrantByIdAsync` 改為回傳 `WarrantDto?`，將 mapping 邏輯從 Controller 移至 Service
8. **[WarrantService.cs / TrialLogService.cs]** 業務邏輯中使用 `WarrantType` Enum 而非 magic string 比較
9. **[TrialLogRepository.cs]** `InsertAsync` 移除不必要的 `BeginTransaction`（或改由 Service 層管理 Transaction scope）
10. **[TrialLogRepository.cs]** `GetRecentByWarrantIdAsync` 的 `count` 參數加入上限驗證（`Math.Clamp`）
11. **[TrialLogsController.cs]** 將 Header 名稱 `"X-Idempotency-Key"` 抽取為常數
