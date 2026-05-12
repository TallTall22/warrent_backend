# 權證系統後端 — 最終 Code Review 報告

**審查日期**：2026-05-11  
**版本**：final（第三輪，前兩輪修正後全量審查）  
**審查範圍**：全部原始碼與交付產出，對照 task.md v4 規格書  
**前輪報告**：`code_review_expert_20260511_v2.md`  
**測試狀態**：35 個單元測試全通過（0 失敗）

---

## 總結評分

| 指標 | 評分 | 說明 |
|------|------|------|
| 金融數值精確度 | ✅ | 全面使用 `decimal`，零 float/double |
| SQL 效能意識 | ✅ | Set-based，QueryMultipleAsync 單次 round-trip |
| 錯誤處理機制 | ✅ | 四層防護：ModelBinding → Controller → Service → Middleware |
| API 冪等性 | ✅ | Pre-check + Race Condition 雙重防護（2627/2601）|
| 核心計算邏輯 | ✅ | CALL/PUT / Delta / HedgeQty 公式均正確 |
| 安全性 | ✅ | 密碼 gitignored，參數化查詢，無 SQL Injection 風險 |
| 架構設計 | ✅ | 分層清晰、DI 解耦、Result Pattern |
| 測試覆蓋 | ✅ | 35 個單元測試全通過 |
| **README.md** | ❌ | **僅有一行標題，完全不符合 task.md 提交要求** |

---

## 一、task.md 核心指標逐項驗證

### 1. 金融數值精確度（decimal）✅

所有金融欄位一致使用 `decimal`：

| 層級 | 欄位 | 型別 |
|------|------|------|
| SQL Schema | Strike_Price, Market_Price, Theory_Price | DECIMAL(18,4) |
| SQL Schema | Hedge_Qty, Conversion_Ratio | DECIMAL(18,4) / DECIMAL(18,2) |
| C# Entity | StrikePrice, MarketPrice, TheoryPrice, HedgeQty, ConversionRatio | decimal |
| C# DTO | 同上 | decimal |
| C# 計算 | `Math.Max(0m, ...)`, `0.8m`, `0.5m`, `0.2m` | decimal literals |

無任何 `float` / `double` 用於金融計算。

### 2. SQL Set-based 效能優化 ✅

- `GetListAsync`：`QueryMultipleAsync` 單次 round-trip 同時取 COUNT + 分頁資料
- 關鍵字搜尋：`LIKE @Keyword + '%'` 前綴比對，可走 PK Range Scan
- `GetRecentByWarrantIdAsync`：`TOP (@Count) ORDER BY Log_ID DESC` 走複合索引 `IX_TrialLog_WarrantId_LogId`
- 冪等查詢：`WHERE Idempotency_Key = @IdempotencyKey` 走唯一約束索引

### 3. 錯誤處理機制 ✅

| 層級 | 位置 | 範圍 |
|------|------|------|
| ModelBinding 驗證 | `[Range]`、`[Required]` | 格式與範圍非法輸入 |
| Controller 驗證 | warrantId 長度、Key 格式 | 路徑參數合法性 |
| Service 業務驗證 | `if (marketPrice <= 0m)` / `if (warrant is null)` | 業務規則 |
| SQL Exception 捕捉 | `catch (SqlException ex) when (2627/2601)` | Race Condition 恢復 |
| 全域兜底 | `ExceptionHandlingMiddleware` | 任何未處理例外 → 統一 500 JSON |

### 4. API 冪等性 ✅

完整三段式路徑：

```
① 冪等前置查詢 → 已存在 → 直接回傳（IsNewRecord=false）
② INSERT 嘗試 → 成功 → 回傳新記錄（IsNewRecord=true）
③ SqlException 2627/2601 → Recovery 再查 → 回傳已提交記錄（IsNewRecord=false）
```

重複請求不觸發 warrant 存在性查詢（最佳化順序正確）。

### 5. 核心計算邏輯 ✅

```
CALL 理論價值 = Max(0, (marketPrice - strikePrice) × conversionRatio)
PUT  理論價值 = Max(0, (strikePrice - marketPrice) × conversionRatio)
避險張數     = positionQty × conversionRatio × delta
Delta        = ITM:0.8 / ATM:0.5 / OTM:0.2
```

與 task.md 規格完全一致，`Math.Max(0m, ...)` 正確截斷負值，`CalculateDelta` 對 CALL/PUT 分別判斷 ITM/OTM 邏輯正確。

---

## 二、本輪新發現問題

### 🔴 高優先級（影響提交評分）

#### [SUBMIT] README.md 實質上是空的，不符合 task.md 明確交付要求

**位置**：`README.md`（根目錄）

**問題**：目前 README.md 僅有一行內容：

```markdown
# warrent_backend
```

而 task.md **評選重點**中明確列出 README 是評分項目，並要求包含：

> README 文件（包含執行說明、架構設計理由）  
> 架構說明、金融數值精確度處理說明、AI 工具（如 VibeCoding）使用與審核報告

**影響**：這是提交的硬性要求。面試官收到 Git Repo 後，README 是第一個被開啟的文件。空白的 README 直接傳達出「產品未完成」的印象，即使後端程式碼品質良好也可能被扣分。

**需補充的 README 章節**：
1. 專案說明與架構圖（分層：Controller → Service → Repository → DB）
2. 快速啟動說明（Docker SQL Server → 執行 SQL Scripts → dotnet run）
3. 金融數值精確度說明（為何使用 decimal，不用 float/double）
4. API 冪等性設計說明（X-Idempotency-Key 機制與 Race Condition 防護）
5. SQL Set-based 效能說明（QueryMultiple、索引設計）
6. AI 工具使用聲明（Claude Code 輔助開發，含 Code Review 報告路徑）
7. 測試執行方式（`dotnet test`）

---

### 🟡 中優先級（程式碼品質）

#### [DESIGN] `SaveAsync` 信任客戶端計算的 `TheoryPrice` 和 `HedgeQty`

**位置**：`Services/TrialLogService.cs:70–79`、`DTOs/SaveTrialLogRequest.cs`

**問題**：`POST /trial-logs` 的請求 Body 包含 `MarketPrice`、`TheoryPrice`、`HedgeQty` 三個欄位。服務端將這三個值直接寫入資料庫，**不重新驗算** `TheoryPrice` 和 `HedgeQty` 是否與 `MarketPrice` 一致：

```csharp
var log = new WarrantTrialLog
{
    MarketPrice = request.MarketPrice,
    TheoryPrice = request.TheoryPrice,  // ← 直接信任客戶端值
    HedgeQty    = request.HedgeQty,     // ← 直接信任客戶端值
    ...
};
```

客戶端若傳入不一致的數值（如因 Bug 或惡意行為），資料庫會存入與市價不符的理論價值和避險數量。對金融系統而言，這是資料完整性風險。

**建議**：在 `SaveAsync` 中，使用 `warrantId` 查到的 warrant 資料，對 `MarketPrice` 重新計算 `TheoryPrice` 和 `HedgeQty`，確保資料庫中存的是伺服器驗證後的值：

```csharp
// 在取得 warrant 之後，重新計算以確保資料完整性
var theoryPrice = CalculateTheoryPrice(warrant, request.MarketPrice);
var hedgeQty    = CalculateHedgeQty(warrant, request.MarketPrice);
```

**注意**：若修改此設計，`SaveTrialLogRequest` 的 `TheoryPrice` 和 `HedgeQty` 欄位即可移除，請求 Body 只需 `MarketPrice`。

---

#### [DEAD CODE] `WarrantType` enum 宣告後從未使用

**位置**：`Domain/Enums/WarrantType.cs`

**問題**：

```csharp
public enum WarrantType { CALL, PUT }
```

此 enum 定義在 `Domain/Enums/` 目錄下，但整個程式碼庫中的 `WarrantType` 屬性全部使用 `string` 型別：

- `WarrantMaster.WarrantType` → `string`
- `WarrantDto.WarrantType` → `string`
- `TrialCalculationDto.WarrantType` → `string`
- `WarrantService.CalculateDelta` 中使用 `warrantType.Equals("CALL", ...)`

這個 enum 是 Dead Code，永遠不會被呼叫。存在容易讓維護者誤以為應該使用它，卻找不到使用之處而困惑。

**建議**：選擇其一：
- 刪除 enum，改用 `const string` 常數（`"CALL"`, `"PUT"`）統一管理魔術字串
- 或全面改用 enum（需調整 Dapper mapping 加型別轉換，工程量較大）

---

#### [TYPE SAFETY] `[Range]` attribute 使用 `double.MaxValue` 套用於 `decimal` 屬性

**位置**：`DTOs/SaveTrialLogRequest.cs:13`、`DTOs/CalculateRequest.cs:12`

**問題**：

```csharp
[Range(0.0001, double.MaxValue, ErrorMessage = "標的價格必須大於零")]
public decimal MarketPrice { get; set; }
```

`[Range(double, double)]` 套用在 `decimal` 屬性時，ASP.NET Core 會將 `double.MaxValue`（≈ 1.8×10³⁰⁸）轉換為 `decimal`，但 `decimal.MaxValue` 僅約 7.9×10²⁸。若客戶端傳入一個超過 `decimal.MaxValue` 但小於 `double.MaxValue` 的數值，可能在 model binding 期間拋出 `OverflowException`，而非回傳乾淨的 400。

**建議**：改用型別安全的 `typeof(decimal)` 版本：

```csharp
[Range(typeof(decimal), "0.0001", "9999999999999999.9999",
    ErrorMessage = "標的價格必須大於零")]
public decimal MarketPrice { get; set; }
```

---

### 🟢 低優先級（輕微問題）

#### [SQL] `03_seed_data.sql` 使用 WHILE 迴圈逐筆 INSERT

**位置**：`Database/03_seed_data.sql`

**問題**：Seed Script 使用 `WHILE @i <= 800 BEGIN ... END` 做 800 次單筆 INSERT，技術上屬於 Loop-based 操作，與 task.md 強調的 "Set-based" 原則相違。

**說明**：Seed script 是一次性執行、非 runtime 路徑，效能影響極小。然而評選時若面試官注意到此處，可能會對 "SQL 效能意識" 評分產生影響。

**建議改法**：使用 CTE 搭配 `sys.objects` 或 `VALUES` 批次插入，做到完整 Set-based：

```sql
WITH Nums AS (
    SELECT TOP 800 ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
    FROM sys.objects a CROSS JOIN sys.objects b
)
INSERT INTO Warrant_Master (Warrant_ID, Strike_Price, Conversion_Ratio, Warrant_Type, Position_Qty)
SELECT
    RIGHT('00000' + CAST(n AS VARCHAR), 5) + CASE WHEN n % 2 = 1 THEN 'C' ELSE 'P' END,
    CAST(10 + ((n - 1) % 99) * 10 AS DECIMAL(18,4)),
    CASE n % 5 WHEN 0 THEN 0.0500 WHEN 1 THEN 0.1000 WHEN 2 THEN 0.2000
               WHEN 3 THEN 0.5000 ELSE 1.0000 END,
    CASE WHEN n % 2 = 1 THEN 'CALL' ELSE 'PUT' END,
    100 + ((n - 1) * 125) % 99901
FROM Nums;
```

---

#### [CONFIG] CORS origin 硬編碼於 `Program.cs`

**位置**：`Program.cs:45`

```csharp
policy.WithOrigins("http://localhost:5173")
```

Frontend origin 應從 `appsettings.json` 讀取，以利不同環境部署：

```json
// appsettings.json
"Cors": { "AllowedOrigins": ["http://localhost:5173"] }
```

---

#### [NAMING] README.md 標題有 typo

**位置**：`README.md:1`

```markdown
# warrent_backend   ← 多一個 r
```

應為 `warrant_backend`。

---

## 三、各輪修正確認彙整

| 輪次 | 問題 | 狀態 |
|------|------|------|
| 第一輪 P0 | SA 密碼明文 | ✅ 已修正 |
| 第一輪 P1 | SaveAsync 未驗證 warrantId 存在 | ✅ 已修正 |
| 第一輪 P2 | InsertAsync 不必要的 Transaction | ✅ 已移除 |
| 第一輪 P3 | 所有 Repository 多餘 conn.Open() | ✅ 已全部移除 |
| 第一輪 P4 | warrantId 路徑參數無長度驗證 | ✅ 三個 Action 均已加入 |
| 第二輪 P1 | 冪等檢查順序：idempotency 應先於 warrantId 查詢 | ✅ 已修正 |
| 第二輪 P2 | GetRecent GET 端點缺少 warrantId 長度驗證 | ✅ 已修正 |
| 第二輪 P3 | 未使用的 `using System.Data` | ✅ 已移除 |
| 第二輪 P4 | InsertAsync 直接修改傳入參數（副作用）| ✅ 改回傳新物件 |
| 第二輪 P5 | GetRecentByWarrantIdAsync 多查 Idempotency_Key | ✅ 已移除 |
| **本輪 P1** | **README.md 空白** | ❌ **待補充** |
| **本輪 P2** | **SaveAsync 信任客戶端計算值** | ⚠️ 設計選擇，建議評估 |
| **本輪 P3** | **WarrantType enum Dead Code** | ❌ **待清除** |
| **本輪 P4** | **[Range] double.MaxValue on decimal** | ❌ **待修正** |
| **本輪 P5** | **03_seed_data.sql WHILE loop** | ⚠️ 建議改為 Set-based |
| **本輪 P6** | **CORS origin 硬編碼** | ⚠️ 低影響，可選修正 |
| **本輪 P7** | **README typo** | ❌ **待修正** |

---

## 四、改善優先順序

| 優先級 | 項目 | 工時估計 | 理由 |
|--------|------|---------|------|
| **P1** | **補齊 README.md** | 30 分鐘 | 評選硬性要求，影響最大 |
| P2 | 修正 `[Range]` type safety | 5 分鐘 | 防止 OverflowException |
| P3 | 刪除 `WarrantType` enum Dead Code | 2 分鐘 | 消除維護混淆 |
| P4 | 改寫 `03_seed_data.sql` 為 Set-based | 10 分鐘 | 強化 SQL 效能意識評分 |
| P5 | 評估 SaveAsync 是否應伺服器重新計算 | 20 分鐘 | 金融資料完整性 |
| P6 | CORS origin 移至 config | 5 分鐘 | 環境彈性 |
| P7 | 修正 README typo | 1 分鐘 | 細節品質 |

---

## 五、架構亮點（維持高品質）

| 設計 | 評價 |
|------|------|
| `Result<T>` Pattern | 業務錯誤以回傳值表達，不以例外驅動控制流程 |
| `ExceptionHandlingMiddleware` | 確保任何未捕捉例外都不洩漏 stack trace |
| `sealed class` 全面使用 | 防止意外繼承，JIT de-virtualization 優化 |
| `QueryMultiple` 批次查詢 | 一次 round-trip 取 COUNT + data |
| 結構化日誌 | 含具名參數 `{WarrantId}`、`{LogId}`，可供日誌系統索引 |
| 跨平台時區處理 | `OperatingSystem.IsWindows()` 確保 Windows/Linux 均正確轉換台灣時間 |
| 冪等鍵順序最佳化 | 重複請求在冪等前置查詢即短路，不觸發 warrantId DB 查詢 |
| Race Condition 雙重防護 | SqlException 2627 與 2601 均有 recovery 路徑並有測試覆蓋 |
| MockBehavior.Strict 測試 | 確保邏輯短路時無額外 DB 呼叫 |

---

## 六、測試覆蓋摘要

| 測試類別 | 數量 | 覆蓋情境 |
|----------|------|---------|
| `WarrantServiceTests` | 12 | 輸入驗證 × 3、warrantId 不存在、CALL/PUT × 3 Delta、理論價值下限 × 2、HedgeQty 計算 |
| `TrialLogServiceTests` | 6 | 非法價格、warrantId 不存在、冪等重複、新記錄、Race Condition 2627、Race Condition 2601 |
| `TrialLogsControllerTests` | 6 | 缺 Key、非法 Key、Service Failure、201 新增、200 重複、warrantId 超長 |
| `WarrantsControllerTests` | 9 | 分頁查詢、關鍵字搜尋、GetById 存在、404、Calculate 成功、非法價格、warrantId 不存在、GetById 超長、Calculate 超長 |
| **合計** | **35** | **全數通過** |
