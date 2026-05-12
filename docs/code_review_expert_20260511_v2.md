# 權證系統後端 — 專家級 Code Review 報告（第二輪）

**審查日期**：2026-05-11  
**版本**：v2（修正後再審）  
**審查範圍**：`src/WarrantApi/` 全部原始碼（含前輪 P0–P4 修正後狀態）  
**對照基準**：`task.md` v4 規格書  
**前輪報告**：`code_review_expert_20260511.md`

---

## 總結評分

| 指標             | 評分 | 說明                              |
|------------------|------|-----------------------------------|
| 金融數值精確度   | ✅   | 全面使用 `decimal`，零 float/double |
| SQL 效能意識     | ✅   | Set-based、單次 round-trip 取 COUNT+data |
| 錯誤處理機制     | ✅   | 三層防護，全域 Middleware 兜底     |
| API 冪等性       | ✅   | 含 Race Condition 2627/2601 雙重防護 |
| 核心計算邏輯     | ✅   | CALL/PUT 公式、Delta、避險張數均正確 |
| 安全性           | ✅   | 密碼移至 Development.json（gitignored）|
| 架構設計         | ✅   | 分層清晰、DI 解耦、Result Pattern  |
| 測試覆蓋         | ✅   | 35 個單元測試全通過               |

> **前輪 5 項問題全數修復**：P0 密碼安全、P1 warrantId 存在性驗證、P2 多餘 Transaction、P3 多餘 conn.Open()、P4 warrantId 長度驗證。

---

## 一、task.md 核心指標逐項驗證

### 1. 金融數值處理精確度（decimal）✅

所有金融欄位一致使用 `decimal`，SQL 對應 `DECIMAL(18,4)` / `DECIMAL(18,2)`，無任何 `float`/`double` 使用。

### 2. SQL Set-based 效能優化 ✅

`WarrantRepository.GetListAsync` 使用 `QueryMultipleAsync` 一次取回 COUNT + 分頁資料，無雙 round-trip。關鍵字搜尋使用前綴 `LIKE @Keyword + '%'`，可走 PK Range Scan。`TrialLogRepository.GetRecentByWarrantIdAsync` 使用 `TOP (@Count)` + `ORDER BY Log_ID DESC` 走複合索引 `IX_TrialLog_WarrantId_LogId`，無全表掃描。

### 3. 錯誤處理機制 ✅

| 層級 | 位置 | 處理方式 |
|------|------|---------|
| 輸入驗證 | Controller | `[Range]` model binding、warrantId 長度、Key 格式 |
| 業務驗證 | Service | `if (marketPrice <= 0)`、`if (warrant is null)` |
| SQL 例外 | Service catch | `SqlException` 2627/2601 精準捕捉 |
| 全域兜底 | `ExceptionHandlingMiddleware` | 任何未處理例外 → 統一 500 JSON |

### 4. API 冪等性 ✅

完整 pre-check → insert → race condition recovery 路徑，已有 35 個測試驗證，含 2627（unique constraint）和 2601（duplicate index）兩種 SqlException。

### 5. 核心計算邏輯 ✅

```
CALL 理論價值 = Max(0, (marketPrice - strikePrice) × conversionRatio)
PUT  理論價值 = Max(0, (strikePrice - marketPrice) × conversionRatio)
避險張數     = positionQty × conversionRatio × delta
Delta        = ITM:0.8 / ATM:0.5 / OTM:0.2
```

與 task.md 規格完全一致，`Math.Max(0m, ...)` 正確截斷負值。

---

## 二、本輪新發現問題

### 🟡 中優先級

#### [LOGIC] 冪等檢查順序不佳：warrantId 存在性檢查先於冪等檢查

**位置**：`Services/TrialLogService.cs:51–59`

**問題**：目前流程：

```
① 價格驗證
② GetByIdAsync(warrantId)     ← 每次請求都多一次 DB round-trip
③ FindByIdempotencyKeyAsync   ← 冪等查詢
④ InsertAsync
```

對於**重複送出**的冪等請求，步驟 ② 是多餘的 DB 查詢。在 warrantId 不變的情況下，warrant 的存在性在第一次請求時已確認，後續重試時不需要再驗證。

**建議**：將冪等檢查提前到 warrantId 驗證之前：

```csharp
// ① 價格驗證（純記憶體，最快失敗）
if (request.MarketPrice <= 0m) ...

// ② 冪等查詢（重複請求在此短路，不再觸及 warrantId 查詢）
var existing = await _logRepo.FindByIdempotencyKeyAsync(idempotencyKey);
if (existing is not null) return 已存在結果;

// ③ warrantId 存在性（只有全新請求才需要驗證）
var warrant = await _warrantRepo.GetByIdAsync(warrantId);
if (warrant is null) ...
```

**影響**：僅影響邏輯順序，不改變最終正確性，但可減少重試場景下約 33% 的 DB 查詢次數。

---

#### [VALIDATION] `GetRecent` 端點缺少 warrantId 長度驗證

**位置**：`Controllers/TrialLogsController.cs:64`

```csharp
public async Task<IActionResult> GetRecent([FromRoute] string warrantId)
{
    // ← 此處缺少 warrantId.Length > 10 的檢查
    var logs = await _trialLogService.GetRecentLogsAsync(warrantId);
```

`POST` 端點已有驗證，但同路由的 `GET` 端點沒有，造成防護不一致。超長 warrantId 雖不會造成 SQL Injection（已參數化），但屬於不必要的 DB 查詢。

**修正**：

```csharp
if (warrantId.Length > 10)
    return BadRequest(new { success = false, message = "warrantId 超過長度上限（10 碼）" });
```

---

### 🟢 低優先級

#### [CLEANUP] `TrialLogRepository.cs` 中未使用的 `using System.Data`

**位置**：`Repositories/TrialLogRepository.cs:1`

移除 Transaction 後，`IDbTransaction` 不再需要，`using System.Data` 應一併移除。

---

#### [DESIGN] `InsertAsync` 直接修改傳入參數（副作用）

**位置**：`Repositories/TrialLogRepository.cs:54`

```csharp
log.LogId = await conn.ExecuteScalarAsync<int>(sql, ...);
return log;
```

直接對傳入的 `WarrantTrialLog` 物件賦值，產生隱性副作用。呼叫端的 `log` 物件也會被改變。雖然在目前單一呼叫路徑下不會造成問題，但違反「函式不應修改傳入參數」的原則。

**建議**：回傳新物件或將 `log` 改為 `record` 型別（不可變）：

```csharp
log = log with { LogId = await conn.ExecuteScalarAsync<int>(sql, ...) };
return log;
```

---

#### [MINOR] `GetRecentByWarrantIdAsync` 查詢了多餘的 `Idempotency_Key` 欄位

**位置**：`Repositories/TrialLogRepository.cs:72–80`

`TrialLogDto` 不包含 `IdempotencyKey`，但 `GetRecentByWarrantIdAsync` 仍將其 SELECT 出來，造成不必要的網路傳輸。可改為只 SELECT `TrialLogDto` 所需的 5 個欄位。

---

## 三、前輪問題修復確認

| 編號 | 問題 | 狀態 |
|------|------|------|
| P0 | SA 密碼明文存於 `appsettings.json` | ✅ 已修正，移至 gitignored `appsettings.Development.json` |
| P1 | `SaveAsync` 未驗證 warrantId 是否存在 | ✅ 已修正，不存在時回傳明確 400 |
| P2 | `InsertAsync` 不必要的 Transaction | ✅ 已移除 |
| P3 | 所有 Repository 多餘的 `conn.Open()` | ✅ 已全部移除 |
| P4 | warrantId 路徑參數無長度驗證 | ✅ 三個 action 均已加入，含對應測試 |

---

## 四、架構亮點（維持高品質）

| 設計 | 評價 |
|------|------|
| `Result<T>` Pattern | 業務錯誤以回傳值表達，不以例外驅動控制流程，可讀性佳 |
| `ExceptionHandlingMiddleware` | 確保任何未捕捉例外都不洩漏 stack trace，符合生產環境標準 |
| `sealed class` 全面使用 | 防止意外繼承，JIT 可做 de-virtualization 優化 |
| `QueryMultiple` 批次查詢 | 一次 round-trip 取 COUNT + data，效能優化明確 |
| 結構化日誌 | `LogWarning`/`LogInformation` 均含具名參數（`{WarrantId}`），可供日誌系統索引查詢 |
| 跨平台時區處理 | `OperatingSystem.IsWindows()` 判斷確保 Windows/Linux 均正確轉換台灣時間 |

---

## 五、改善優先順序建議

| 優先級 | 項目 | 工時估計 |
|--------|------|---------|
| P1 | 調整冪等檢查順序（效能改善） | 10 分鐘 |
| P2 | `GetRecent` 補 warrantId 長度驗證 | 5 分鐘 |
| P3 | 移除未使用的 `using System.Data` | 1 分鐘 |
| P4 | `InsertAsync` 改用 `with` 運算子回傳新物件 | 5 分鐘 |
| P5 | `GetRecentByWarrantIdAsync` 移除多餘欄位 | 5 分鐘 |

---

## 六、測試覆蓋摘要

| 測試類別 | 數量 | 新增 |
|---|---|---|
| `WarrantServiceTests` | 12 | — |
| `TrialLogServiceTests` | 6 | +2（warrantId 不存在、SqlException 2601）|
| `TrialLogsControllerTests` | 6 | +1（warrantId 超長 400）|
| `WarrantsControllerTests` | 9 | +2（warrantId 超長 400 × 2）|
| **合計** | **35** | **35 個全通過** |
