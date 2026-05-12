# 權證發行風險監控與避險試算系統 — 後端

C# .NET 10 Web API，實作權證（Warrant）理論價值試算、Delta 避險數量計算與試算記錄管理。

---

## 快速啟動

### 前置需求

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/)

### 步驟 1：啟動 SQL Server（Docker）

```bash
docker compose up -d
```

容器名稱：`warrant-sqlserver`，Port：`1433`，SA 密碼：`Warrant@2025`。

### 步驟 2：建立資料庫與測試資料

使用任意 SQL 客戶端（SSMS、Azure Data Studio、DataGrip）連線後，依序執行：

```sql
-- 1. 建表與 CHECK 約束
Database/01_create_tables.sql

-- 2. 建立效能索引
Database/02_create_indexes.sql

-- 3. 植入 800 筆測試資料（Set-based CTE，無 WHILE 迴圈）
Database/03_seed_data.sql
```

連線資訊：Server `localhost,1433`、User `sa`、Password `Warrant@2025`。

### 步驟 3：啟動 API

```bash
dotnet run --project src/WarrantApi/WarrantApi.csproj
```

啟動後可開啟 `http://localhost:5226`（Swagger UI）驗證所有端點。

### 步驟 4：執行單元測試

```bash
dotnet test src/WarrantApi.Tests/WarrantApi.Tests.csproj
```

35 個測試全數通過，包含 WarrantService、TrialLogService、兩個 Controller。

---

## 系統架構

```
┌─────────────────────────────────────────────────────┐
│                  HTTP Clients                       │
└────────────────────┬────────────────────────────────┘
                     │ HTTP/JSON
┌────────────────────▼────────────────────────────────┐
│           ExceptionHandlingMiddleware               │  ← 全域例外兜底，統一 500 回應
├─────────────────────────────────────────────────────┤
│  WarrantsController   │  TrialLogsController        │  ← HTTP 語意（無業務邏輯）
├─────────────────────────────────────────────────────┤
│  WarrantService       │  TrialLogService             │  ← 業務規則、Result Pattern
│                       │                              │
│              WarrantCalculator                       │  ← 純計算（單一來源）
├─────────────────────────────────────────────────────┤
│  WarrantRepository    │  TrialLogRepository          │  ← Dapper Set-based SQL
├─────────────────────────────────────────────────────┤
│                   AppDbContext                      │  ← IDbConnection 工廠
├─────────────────────────────────────────────────────┤
│              SQL Server 2022 (Docker)               │
└─────────────────────────────────────────────────────┘
```

### 設計決策

| 決策 | 理由 |
|------|------|
| Dapper（非 EF Core）| 直接控制 SQL，Set-based 查詢更明確，避免 N+1 |
| Result Pattern | 業務錯誤以回傳值表達，不用例外驅動控制流程 |
| sealed class 全面使用 | 防止意外繼承，JIT de-virtualization 優化 |
| WarrantCalculator 共用 | 計算邏輯單一來源，WarrantService 與 TrialLogService 共用 |

---

## 金融數值精確度

所有金融數值**全面使用 `decimal`**，禁止 `float` / `double`。

| 為何不用 float/double | 說明 |
|-----------------------|------|
| IEEE 754 二進位浮點 | `0.1 + 0.2 ≠ 0.3`，財務計算誤差不可接受 |
| decimal 十進位精度 | `decimal` 使用十進位運算，無此問題 |

對應關係：

| C# 型別 | SQL 型別 | 欄位 |
|---------|---------|------|
| `decimal` | `DECIMAL(18,4)` | Strike_Price、Market_Price、Theory_Price、Conversion_Ratio |
| `decimal` | `DECIMAL(18,2)` | Hedge_Qty |
| `int` | `INT` | Position_Qty（張數，整數） |

計算中所有字面值均使用 `decimal` 後綴（`0.8m`、`0.5m`、`0m`），不混用其他數值型別。

---

## API 冪等性設計

儲存試算記錄（`POST /api/warrants/{id}/trial-logs`）採用 **X-Idempotency-Key** 機制：

```
請求 Header：X-Idempotency-Key: {UUID}
```

### 流程

```
① 市場價格驗證（純記憶體，最快失敗）
② FindByIdempotencyKeyAsync → 已存在 → 回傳 200 OK（IsNewRecord=false）
③ GetByIdAsync(warrantId) → 不存在 → 回傳 400（只對全新請求驗證）
④ 伺服器重算 TheoryPrice、HedgeQty（不信任客戶端計算值）
⑤ InsertAsync → 成功 → 回傳 201 Created（IsNewRecord=true）
⑥ InsertAsync → SqlException 2627/2601 → FindByIdempotencyKeyAsync（Recovery）→ 200 OK
```

### Race Condition 防護

並發的相同 Key 請求同時通過步驟 ②，其中一個先 INSERT 成功，另一個拋出 `SqlException 2627`（唯一約束違反）或 `2601`（唯一索引違反），透過 Recovery 查詢取回已提交的記錄，確保雙方均回傳 200 OK 而不是 500。

### HTTP 語意

| 情況 | HTTP Status |
|------|-------------|
| 全新寫入 | `201 Created` |
| 冪等重複 / Race Condition | `200 OK` |
| 驗證失敗 | `400 Bad Request` |

---

## SQL 效能設計

### Set-based 查詢

| 查詢 | 做法 |
|------|------|
| 分頁清單（COUNT + 資料）| `QueryMultipleAsync` 單次 round-trip |
| 關鍵字搜尋 | `LIKE @Keyword + '%'` 前綴比對，走 PK Range Scan |
| 最近 N 筆試算記錄 | `TOP (@Count) ORDER BY Log_ID DESC` |
| 冪等鍵查詢 | `WHERE Idempotency_Key = @IdempotencyKey`，走唯一約束索引 |

**禁止在應用程式層 Loop 查詢資料庫。**

### 索引設計

```sql
-- 試算記錄：依 Warrant_ID 查最新 N 筆（核心查詢模式）
CREATE INDEX IX_TrialLog_WarrantId_LogId
    ON Warrant_Trial_Log (Warrant_ID ASC, Log_ID DESC);

-- 冪等鍵：唯一約束自動建立索引
CONSTRAINT UQ_TrialLog_Idempotency UNIQUE (Idempotency_Key)
```

### Seed Data（Set-based CTE）

測試資料採用 CTE + `CROSS JOIN` 方式生成，無 `WHILE` 迴圈：

```sql
WITH
Digits AS (SELECT 0 AS d UNION ALL ... UNION ALL SELECT 9),
Nums AS (
    SELECT TOP 800 ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
    FROM Digits d1 CROSS JOIN Digits d2 CROSS JOIN Digits d3
)
INSERT INTO Warrant_Master (...)
SELECT ... FROM Nums;
```

---

## 錯誤處理

| 層級 | 機制 | 範圍 |
|------|------|------|
| ModelBinding | `[Range]`、`[Required]` | 型別與格式驗證 |
| Controller | warrantId 長度、Key UUID 格式 | 路徑/Header 參數 |
| Service | `Result<T>` Pattern | 業務規則（價格 > 0、warrant 存在性）|
| Repository | `SqlException` catch（2627/2601）| Race Condition Recovery |
| Middleware | `ExceptionHandlingMiddleware` | 所有未處理例外 → 統一 500 JSON |

---

## API 文件

詳細端點說明、Request/Response 範例請參閱 [`docs/api.md`](docs/api.md)。

Swagger UI 在開發模式下可於 `http://localhost:5226` 存取。

---

## 安全性

- **資料庫密碼**：實際密碼存於 `appsettings.Development.json`（已加入 `.gitignore`），`appsettings.json` 僅保留 `CHANGE_ME` 佔位符。
- **SQL Injection**：所有查詢透過 Dapper 參數化，無字串拼接。
- **CORS**：允許的 Frontend Origin 設定於 `appsettings.json` 的 `Cors.AllowedOrigins`，不硬編碼於程式碼。

---

## AI 工具使用聲明

本專案後端使用 **Claude Code**（Anthropic）輔助開發，包含：

- 架構設計建議與 Code Review
- 單元測試撰寫（xUnit + Moq）
- SQL 效能優化建議（QueryMultiple、Set-based Seed）
- 冪等性設計審查（Race Condition 防護）

### Code Review 報告

| 報告 | 說明 |
|------|------|
| [`docs/code_review_20260510.md`](docs/code_review_20260510.md) | 第一輪 Code Review（含 P0 密碼安全等 5 項問題）|
| [`docs/code_review_expert_20260511.md`](docs/code_review_expert_20260511.md) | 第二輪專家級審查（第一輪修正後再審）|
| [`docs/code_review_expert_20260511_v2.md`](docs/code_review_expert_20260511_v2.md) | 第三輪審查（第二輪修正後再審）|
| [`docs/code_review_final_20260511.md`](docs/code_review_final_20260511.md) | 最終審查（全量掃描，含本次修正清單）|

所有由 AI 產出的程式碼均經人工審查確認邏輯正確，並透過 35 個單元測試驗證行為。

---

## 目錄結構

```
Backend/
├── docker-compose.yml              # SQL Server 2022 容器
├── Database/
│   ├── 01_create_tables.sql        # 建表與約束
│   ├── 02_create_indexes.sql       # 效能索引
│   └── 03_seed_data.sql            # 800 筆測試資料（Set-based CTE）
├── docs/
│   ├── api.md                      # 前端 API 文件
│   └── code_review_*.md            # Code Review 報告
└── src/
    ├── WarrantApi/
    │   ├── Controllers/            # HTTP 輸入/輸出層
    │   ├── Services/               # 業務邏輯層
    │   │   └── WarrantCalculator   # 共用計算邏輯（單一來源）
    │   ├── Repositories/           # 資料存取層（Dapper）
    │   ├── Domain/Entities/        # 資料庫對應實體
    │   ├── DTOs/                   # 請求/回應資料傳輸物件
    │   ├── Common/Result.cs        # Result Pattern
    │   ├── Infrastructure/         # DB 連線工廠
    │   └── Middleware/             # 全域例外處理
    └── WarrantApi.Tests/
        ├── Services/               # Service 單元測試（17 個）
        └── Controllers/            # Controller 單元測試（15 個）
```
