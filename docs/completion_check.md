# 後端完整性檢查報告

**檢查日期**：2026-05-10
**版本**：v1.0
**專案**：權證發行風險監控與避險試算系統
**建置狀態**：dotnet build 成功（0 錯誤、0 警告）

---

## P0 環境建置與專案初始化

- [x] 任務 1：建立 .NET Web API 專案 — `WarrantApi.csproj` 存在，專案結構完整，框架為 .NET 10（計畫書寫 .NET 8，實際使用 .NET 10，為向上升級，無功能影響）
- [x] 任務 2：安裝 NuGet 套件（Dapper、Microsoft.Data.SqlClient、Swashbuckle）— `WarrantRepository.cs` 引用 Dapper，`AppDbContext.cs` 引用 Microsoft.Data.SqlClient，`Program.cs` 引用 AddSwaggerGen
- [x] 任務 3：設定 `appsettings.json` 連線字串 — `AppDbContext` 讀取 `DefaultConnection`，有防護性拋出（connection string 未設定時 throw InvalidOperationException）
- [x] 任務 4：設定 CORS Policy — `Program.cs` 設定 `FrontendPolicy`，允許 `http://localhost:5173`，AllowAnyHeader + AllowAnyMethod
- [x] 任務 5：設定 Swagger / OpenAPI — `Program.cs` 完整設定 AddSwaggerGen + UseSwagger + UseSwaggerUI，根路徑（RoutePrefix = string.Empty）展示 Swagger UI

---

## P0 基礎架構層

- [x] 任務 6：建立 `Result<T>` 通用回傳模型 — `Common/Result.cs` 實作泛型 `Result<T>` 與非泛型 `Result`，包含 `IsSuccess`、`IsFailure`、`Value`、`ErrorMessage` 屬性與 `Success()`、`Failure()` 工廠方法
- [x] 任務 7：建立 `AppDbContext`（Dapper 連線工廠）— `Infrastructure/AppDbContext.cs` 實作 `CreateConnection()` 回傳 `IDbConnection`，Singleton 生命週期正確
- [x] 任務 8：建立 Domain Entities — `WarrantMaster.cs` 與 `WarrantTrialLog.cs` 均已實作，所有金融欄位使用 `decimal`；另有 `WarrantType.cs` Enum
- [x] 任務 9：建立 DTOs — `WarrantDto`、`TrialCalculationDto`、`CalculateRequest`、`SaveTrialLogRequest`、`TrialLogDto`、`PagedResult<T>` 均已實作（計畫書未列 `CalculateRequest` 與 `PagedResult<T>`，為額外實作，有益於型別安全）

---

## P1 Repository 層

- [x] 任務 10：建立 `IWarrantRepository` 介面 — `Repositories/Interfaces/IWarrantRepository.cs` 定義 `GetListAsync` 與 `GetByIdAsync`
- [x] 任務 11：實作 `WarrantRepository.GetListAsync`（分頁 + 關鍵字搜尋） — 使用 `QueryMultipleAsync` 單次往返取得 COUNT + 分頁資料，`LIKE @Keyword + '%'` 前綴搜尋，`OFFSET...FETCH NEXT` 分頁
- [x] 任務 12：實作 `WarrantRepository.GetByIdAsync` — 單一 SELECT，參數化查詢防 SQL Injection
- [x] 任務 13：建立 `ITrialLogRepository` 介面 — `Repositories/Interfaces/ITrialLogRepository.cs` 定義三個方法
- [x] 任務 14：實作 `TrialLogRepository.FindByIdempotencyKeyAsync` — 參數化查詢，回傳 nullable `WarrantTrialLog`
- [x] 任務 15：實作 `TrialLogRepository.InsertAsync`（含 Transaction）— 使用 `IDbConnection.BeginTransaction()`，try-catch-rollback 模式，`SCOPE_IDENTITY()` 取回 LogId
- [x] 任務 16：實作 `TrialLogRepository.GetRecentByWarrantIdAsync`（TOP 10，Set-based）— `SELECT TOP (@Count) ... ORDER BY Log_ID DESC`，無 Loop 查詢

---

## P1 Service 層

- [x] 任務 17：建立 `IWarrantService` 介面 — `Services/Interfaces/IWarrantService.cs` 定義三個方法
- [x] 任務 18：實作 `WarrantService.GetWarrantListAsync` — 呼叫 Repository，組裝 `PagedResult<WarrantDto>`，使用 LINQ Select + MapToDto
- [x] 任務 19：實作 `WarrantService.CalculateAsync` — 含 `marketPrice <= 0` 防護、找不到權證回傳 Failure、Delta/TheoryPrice/HedgeQty 三步計算，符合 plan.md 規格
- [x] 任務 20：建立 `ITrialLogService` 介面 — `Services/Interfaces/ITrialLogService.cs` 定義三個方法（含額外的 `IdempotencyKeyExistsAsync`）
- [x] 任務 21：實作 `TrialLogService.SaveAsync`（冪等性判斷 + 交易寫入 + 例外捕獲）— 包含 marketPrice 驗證、先查冪等 Key、正常寫入、SqlException（2627/2601）Race Condition 處理
- [x] 任務 22：實作 `TrialLogService.GetRecentLogsAsync` — 呼叫 Repository，Select MapToDto

---

## P1 Controller 層

- [x] 任務 23：建立 `WarrantsController`（GET list、GET by id、POST calculate）— 三個 Action 均已實作，無業務邏輯洩漏
- [x] 任務 24：建立 `TrialLogsController`（POST save、GET recent logs）— 兩個 Action 均已實作，冪等 Key Header 驗證在 Controller 完成
- [x] 任務 25：加入全域例外處理 Middleware — `ExceptionHandlingMiddleware.cs` 攔截所有未處理例外，回傳標準化 500，不洩漏 stack trace
- [x] 任務 26：DI 容器註冊 — `Program.cs` 完整註冊 AppDbContext（Singleton）、Repository（Scoped）、Service（Scoped）

---

## P2 品質與驗證

- [x] 任務 27：加入輸入驗證 — `CalculateRequest` 與 `SaveTrialLogRequest` 使用 DataAnnotations（`[Required]`、`[Range]`）；`Program.cs` 有自訂 `InvalidModelStateResponseFactory` 統一錯誤格式；Service 層有第二道驗證防線
- [x] 任務 28：加入 Logging — `WarrantService` 與 `TrialLogService` 均注入 `ILogger<T>`，記錄關鍵操作與例外，使用結構化 Log（參數化）
- [ ] 任務 29：Swagger 測試所有 Endpoints — 無法在靜態分析階段確認（需執行期驗證）；但 Swagger 設定完整，所有 Controller Action 均有 `[ProducesResponseType]` 標注，推斷 Swagger UI 可正常展示

---

## API Endpoints 對照

| Endpoint | 實作狀態 | Route | 備註 |
|----------|----------|-------|------|
| GET /api/warrants | 已實作 | `WarrantsController.GetList` | 支援 keyword、page（預設 1）、pageSize（預設 50，上限 200）|
| GET /api/warrants/{warrantId} | 已實作 | `WarrantsController.GetById` | 404 格式含 success/message |
| POST /api/warrants/{warrantId}/calculate | 已實作 | `WarrantsController.Calculate` | 200 成功、400 失敗 |
| POST /api/warrants/{warrantId}/trial-logs | 已實作 | `TrialLogsController.Save` | 201 新寫入、200 冪等重複、400 驗證失敗 |
| GET /api/warrants/{warrantId}/trial-logs | 已實作 | `TrialLogsController.GetRecent` | 回傳 `{ warrantId, logs: [...] }` 格式 |

---

## 商業邏輯對照

| 邏輯 | plan.md 規格 | 實作狀態 | 說明 |
|------|-------------|----------|------|
| Delta CALL ITM | marketPrice > strikePrice → 0.8 | 符合 | `WarrantService.CalculateDelta` |
| Delta CALL ATM | marketPrice == strikePrice → 0.5 | 符合 | 優先判斷 ATM |
| Delta CALL OTM | marketPrice < strikePrice → 0.2 | 符合 | |
| Delta PUT ITM | marketPrice < strikePrice → 0.8 | 符合 | |
| TheoryPrice CALL | Max(0, (mktPrice - strike) * ratio) | 符合 | |
| TheoryPrice PUT | Max(0, (strike - mktPrice) * ratio) | 符合 | |
| HedgeQty | positionQty * conversionRatio * delta | 符合 | |
| 冪等性 | 相同 key 回傳已存結果 | 符合 | 含 Race Condition 處理 |

---

## 資料庫 Schema 對照

| 項目 | plan.md 規格 | 實作狀態 | 說明 |
|------|-------------|----------|------|
| Warrant_Master DDL | 完整定義 | 符合 | 欄位、型別、CHECK 約束均一致 |
| Warrant_Trial_Log DDL | 完整定義 | 符合 | 含 Idempotency_Key UNIQUE 約束 |
| 主鍵索引 | PK_Warrant_Master / PK_Warrant_Trial_Log | 符合 | |
| 外鍵約束 | FK_TrialLog_Warrant | 符合 | |
| 效能索引 | IX_TrialLog_WarrantId_LogId | 符合 | 02_create_indexes.sql |
| 備用索引 | IX_TrialLog_WarrantId_CreatedTime | 符合 | |

---

## Seed Data 對照

| 項目 | plan.md 規格 | 實作狀態 | 說明 |
|------|-------------|----------|------|
| 總筆數 | 800 筆 | 符合 | WHILE @i <= 800 |
| CALL/PUT 比例 | 各 50%（400 筆） | 符合 | `@i % 2 = 1 → CALL` |
| Warrant_ID 格式 | 5碼數字 + C/P | 符合 | RIGHT('00000' + ..., 5) + C/P |
| 履約價範圍 | 10 ~ 1000 | 符合 | `10 + ((@i-1) % 99) * 10` |
| 行使比例 | 0.05/0.1/0.2/0.5/1.0 | 符合 | CASE (@i % 5) |
| 庫存張數 | 100 ~ 100,000 | 符合 | `100 + ((@i-1) * 125) % 99901` |
| 驗證 Script | SELECT COUNT(*) 等 | 符合 | 附在檔案末尾 |

---

## 缺失項目

1. **plan.md 任務 29（Swagger 執行期測試）**：屬於執行期驗證任務，無法由靜態分析確認，需人工啟動應用程式後測試。
2. **plan.md pageSize 上限為 100**：計畫書規格為「最大 100」，實作中 `WarrantsController.GetList` 的上限設為 `200`，存在規格不一致（偏寬鬆）。
3. **TrialLogsController GetRecent 缺少 404 防護**：當 `warrantId` 不存在時，`GetRecentByWarrantIdAsync` 直接回傳空陣列，不回傳 404，與部分 RESTful 最佳實踐有差距（不過 plan.md 回應格式未明確要求 404）。

---

## 整體完成度

28 / 29 項完成（97%）

計畫書所列 P0/P1/P2 任務中，28 項已完整實作；1 項（Swagger 執行期測試）為執行期驗證任務，靜態分析無法確認。所有 5 支 API Endpoint、商業計算邏輯、冪等性機制、資料庫 Schema、800 筆 Seed Data 均完整實作，dotnet build 0 錯誤通過。
