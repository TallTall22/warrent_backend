# 權證發行風險監控與避險試算系統 — 完整實作計畫

**撰寫日期**：2026-05-10
**版本**：v1.0
**負責人**：後端工程師

---

## 目錄

1. [專案架構設計](#1-專案架構設計)
2. [資料庫規劃](#2-資料庫規劃)
3. [API 設計](#3-api-設計)
4. [後端實作步驟清單](#4-後端實作步驟清單)
5. [前端實作步驟清單](#5-前端實作步驟清單)
6. [測試資料生成計畫](#6-測試資料生成計畫)
7. [風險點與應對策略](#7-風險點與應對策略)
8. [實作順序建議](#8-實作順序建議)

---

## 1. 專案架構設計

### 1.1 整體目錄結構

```
warrantTask/
├── Backend/                          # C# .NET 8 Web API
│   ├── docs/
│   │   └── plan.md                   # 本文件
│   ├── src/
│   │   └── WarrantApi/
│   │       ├── WarrantApi.csproj
│   │       ├── Program.cs
│   │       ├── appsettings.json
│   │       ├── appsettings.Development.json
│   │       ├── Controllers/
│   │       │   ├── WarrantsController.cs
│   │       │   └── TrialLogsController.cs
│   │       ├── Services/
│   │       │   ├── Interfaces/
│   │       │   │   ├── IWarrantService.cs
│   │       │   │   └── ITrialLogService.cs
│   │       │   ├── WarrantService.cs
│   │       │   └── TrialLogService.cs
│   │       ├── Repositories/
│   │       │   ├── Interfaces/
│   │       │   │   ├── IWarrantRepository.cs
│   │       │   │   └── ITrialLogRepository.cs
│   │       │   ├── WarrantRepository.cs
│   │       │   └── TrialLogRepository.cs
│   │       ├── Domain/
│   │       │   ├── Entities/
│   │       │   │   ├── WarrantMaster.cs
│   │       │   │   └── WarrantTrialLog.cs
│   │       │   └── Enums/
│   │       │       └── WarrantType.cs
│   │       ├── DTOs/
│   │       │   ├── WarrantDto.cs
│   │       │   ├── TrialCalculationDto.cs
│   │       │   ├── SaveTrialLogRequest.cs
│   │       │   └── TrialLogDto.cs
│   │       ├── Common/
│   │       │   └── Result.cs
│   │       └── Infrastructure/
│   │           └── AppDbContext.cs
│   └── README.md
├── Frontend/                         # Vue.js 3 + TypeScript
│   ├── src/
│   │   ├── api/
│   │   │   └── warrantsApi.ts
│   │   ├── components/
│   │   │   ├── WarrantList.vue
│   │   │   ├── TrialPanel.vue
│   │   │   └── TrialLogTable.vue
│   │   ├── composables/
│   │   │   └── useTrialCalculation.ts
│   │   ├── stores/
│   │   │   └── warrantsStore.ts
│   │   ├── types/
│   │   │   └── warrant.ts
│   │   ├── App.vue
│   │   └── main.ts
│   ├── package.json
│   └── vite.config.ts
├── Database/
│   ├── 01_create_tables.sql          # 建表 DDL
│   ├── 02_create_indexes.sql         # 索引
│   └── 03_seed_data.sql              # 800 筆測試資料
├── Docs/
└── task.md
```

### 1.2 後端分層設計（嚴格遵守職責邊界）

```
HTTP Request
     |
 Controller         職責：解析 HTTP 輸入、呼叫 Service、回傳 HTTP 輸出
     |               禁止：業務邏輯、直接操作資料庫
 Service            職責：業務規則、計算邏輯、交易管理、輸入驗證
     |               禁止：直接操作 DbContext
 Repository         職責：資料存取（CRUD）、SQL 查詢
     |               禁止：業務邏輯
 DbContext / ADO.NET  職責：連線管理、ORM 對應
```

### 1.3 關鍵設計決策

| 決策 | 選擇 | 理由 |
|------|------|------|
| ORM | Dapper（輕量 Micro-ORM） | 完全掌控 SQL，便於 Set-based 優化，避免 EF Core 產生 N+1 |
| 數值型別 | decimal（C#）/ DECIMAL(18,4)（SQL） | 金融精度要求，絕不使用 float/double |
| 錯誤回傳 | Result Pattern（自訂 Result<T>） | 讓呼叫端強制處理失敗案例，不依賴例外控制流程 |
| 冪等性 | X-Idempotency-Key Header + DB 唯一索引 | 防止網路重試導致重複寫入 |
| CORS | 開發環境開放 localhost:5173 | 前後端分離部署需要 |

---

## 2. 資料庫規劃

### 2.1 Schema DDL

```sql
-- 01_create_tables.sql

-- 權證基本檔
CREATE TABLE Warrant_Master (
    Warrant_ID       VARCHAR(10)    NOT NULL,
    Strike_Price     DECIMAL(18,4)  NOT NULL,
    Conversion_Ratio DECIMAL(18,4)  NOT NULL,
    Warrant_Type     VARCHAR(4)     NOT NULL,   -- 'CALL' or 'PUT'
    Position_Qty     INT            NOT NULL,
    CONSTRAINT PK_Warrant_Master PRIMARY KEY (Warrant_ID),
    CONSTRAINT CK_Warrant_Type   CHECK (Warrant_Type IN ('CALL','PUT')),
    CONSTRAINT CK_Strike_Price   CHECK (Strike_Price > 0),
    CONSTRAINT CK_Conversion_Ratio CHECK (Conversion_Ratio > 0),
    CONSTRAINT CK_Position_Qty   CHECK (Position_Qty >= 0)
);

-- 試算日誌表（含冪等鍵）
CREATE TABLE Warrant_Trial_Log (
    Log_ID           INT            NOT NULL IDENTITY(1,1),
    Warrant_ID       VARCHAR(10)    NOT NULL,
    Market_Price     DECIMAL(18,4)  NOT NULL,
    Theory_Price     DECIMAL(18,4)  NOT NULL,
    Hedge_Qty        DECIMAL(18,2)  NOT NULL,
    Created_Time     DATETIME       NOT NULL DEFAULT GETDATE(),
    Idempotency_Key  UNIQUEIDENTIFIER NOT NULL,
    CONSTRAINT PK_Warrant_Trial_Log     PRIMARY KEY (Log_ID),
    CONSTRAINT FK_TrialLog_Warrant      FOREIGN KEY (Warrant_ID)
        REFERENCES Warrant_Master(Warrant_ID),
    CONSTRAINT UQ_TrialLog_Idempotency  UNIQUE (Idempotency_Key),
    CONSTRAINT CK_Market_Price          CHECK (Market_Price > 0),
    CONSTRAINT CK_Theory_Price          CHECK (Theory_Price >= 0),
    CONSTRAINT CK_Hedge_Qty             CHECK (Hedge_Qty >= 0)
);
```

### 2.2 索引策略

```sql
-- 02_create_indexes.sql

-- 試算日誌：按 Warrant_ID 查詢最新 N 筆（核心查詢模式）
CREATE INDEX IX_TrialLog_WarrantId_LogId
    ON Warrant_Trial_Log (Warrant_ID ASC, Log_ID DESC);

-- 若未來需依時間區間查詢日誌
CREATE INDEX IX_TrialLog_WarrantId_CreatedTime
    ON Warrant_Trial_Log (Warrant_ID ASC, Created_Time DESC);

-- Warrant_Master PK 本身即為 clustered index，不需額外加索引
```

### 2.3 最近 10 筆查詢（Set-based，無 Loop）

```sql
-- 單一權證最近 10 筆試算紀錄
SELECT TOP 10
    Log_ID,
    Warrant_ID,
    Market_Price,
    Theory_Price,
    Hedge_Qty,
    Created_Time
FROM Warrant_Trial_Log
WHERE Warrant_ID = @WarrantId
ORDER BY Log_ID DESC;

-- 若需一次撈多支權證各自最新 N 筆（Set-based，使用視窗函數）
SELECT Warrant_ID, Log_ID, Market_Price, Theory_Price, Hedge_Qty, Created_Time
FROM (
    SELECT *,
           ROW_NUMBER() OVER (PARTITION BY Warrant_ID ORDER BY Log_ID DESC) AS rn
    FROM Warrant_Trial_Log
    WHERE Warrant_ID IN @WarrantIds
) ranked
WHERE rn <= 10;
```

### 2.4 資料完整性設計

- `Strike_Price > 0`：CHECK 約束防止非法資料
- `Market_Price > 0`：API 層驗證 + DB CHECK 雙重防護
- `Idempotency_Key UNIQUE`：資料庫層強制冪等性
- Foreign Key `Warrant_ID`：確保日誌必有對應主檔

---

## 3. API 設計

### 3.1 Endpoints 總覽

| 方法 | 路徑 | 說明 | 冪等性 |
|------|------|------|--------|
| GET | `/api/warrants` | 取得權證列表（支援關鍵字搜尋、分頁） | 天然冪等 |
| GET | `/api/warrants/{warrantId}` | 取得單一權證詳細資料 | 天然冪等 |
| POST | `/api/warrants/{warrantId}/calculate` | 即時試算（不寫入 DB） | 天然冪等（純計算） |
| POST | `/api/warrants/{warrantId}/trial-logs` | 儲存試算結果 | X-Idempotency-Key |
| GET | `/api/warrants/{warrantId}/trial-logs` | 取得最近 10 筆試算紀錄 | 天然冪等 |

### 3.2 Request / Response DTO

#### GET /api/warrants

**Query Parameters**

| 參數 | 型別 | 必填 | 說明 |
|------|------|------|------|
| keyword | string | 否 | 權證代碼關鍵字搜尋 |
| page | int | 否 | 頁碼（預設 1） |
| pageSize | int | 否 | 每頁筆數（預設 50，最大 100） |

**Response 200**

```json
{
  "data": [
    {
      "warrantId": "03001P",
      "strikePrice": 100.5000,
      "conversionRatio": 0.1000,
      "warrantType": "CALL",
      "positionQty": 5000
    }
  ],
  "total": 800,
  "page": 1,
  "pageSize": 50
}
```

#### POST /api/warrants/{warrantId}/calculate

**Request Body**

```json
{
  "marketPrice": 105.00
}
```

**Response 200（試算成功）**

```json
{
  "warrantId": "03001P",
  "marketPrice": 105.00,
  "strikePrice": 100.5000,
  "conversionRatio": 0.1000,
  "warrantType": "CALL",
  "positionQty": 5000,
  "delta": 0.8,
  "deltaStatus": "ITM",
  "theoryPrice": 0.4500,
  "hedgeQty": 400.00
}
```

**Response 400（輸入非法）**

```json
{
  "success": false,
  "message": "標的價格必須大於零"
}
```

#### POST /api/warrants/{warrantId}/trial-logs

**Headers**

```
X-Idempotency-Key: {UUID v4}
Content-Type: application/json
```

**Request Body**

```json
{
  "marketPrice": 105.00,
  "theoryPrice": 0.4500,
  "hedgeQty": 400.00
}
```

**Response 201（寫入成功）**

```json
{
  "logId": 1001,
  "warrantId": "03001P",
  "marketPrice": 105.00,
  "theoryPrice": 0.4500,
  "hedgeQty": 400.00,
  "createdTime": "2026-05-10T11:30:00"
}
```

**Response 200（冪等重複請求，回傳已存結果）**

```json
{
  "logId": 1001,
  "warrantId": "03001P",
  "marketPrice": 105.00,
  "theoryPrice": 0.4500,
  "hedgeQty": 400.00,
  "createdTime": "2026-05-10T11:30:00"
}
```

**Response 400（marketPrice <= 0）**

```json
{
  "success": false,
  "message": "標的價格必須大於零，禁止存檔"
}
```

#### GET /api/warrants/{warrantId}/trial-logs

**Response 200**

```json
{
  "warrantId": "03001P",
  "logs": [
    {
      "logId": 1001,
      "marketPrice": 105.00,
      "theoryPrice": 0.4500,
      "hedgeQty": 400.00,
      "createdTime": "2026-05-10T11:30:00"
    }
  ]
}
```

### 3.3 冪等性防護機制

```
前端生成 UUID v4 → 放入 Header X-Idempotency-Key
     |
Controller 讀取 Header → 傳入 Service
     |
Service 呼叫 Repository 查詢是否已存在相同 Key
     |
已存在 → 直接回傳已存結果（HTTP 200）
不存在 → 正常寫入（HTTP 201）
     |
若兩個請求同時抵達 → DB UNIQUE 約束攔截第二筆
→ 捕獲 SqlException（2627 violation）→ 查詢並回傳已存結果
```

### 3.4 核心計算邏輯（C# 實作規格）

```csharp
// 計算 Delta 狀態
private static (decimal delta, string status) CalculateDelta(
    string warrantType, decimal marketPrice, decimal strikePrice)
{
    bool isCall = warrantType.Equals("CALL", StringComparison.OrdinalIgnoreCase);

    if (marketPrice == strikePrice)
        return (0.5m, "ATM");

    bool isITM = isCall
        ? marketPrice > strikePrice
        : marketPrice < strikePrice;

    return isITM ? (0.8m, "ITM") : (0.2m, "OTM");
}

// 計算理論價值
private static decimal CalculateTheoryPrice(
    string warrantType, decimal marketPrice,
    decimal strikePrice, decimal conversionRatio)
{
    decimal intrinsicValue = warrantType.Equals("CALL", StringComparison.OrdinalIgnoreCase)
        ? marketPrice - strikePrice
        : strikePrice - marketPrice;

    return Math.Max(0m, intrinsicValue * conversionRatio);
}

// 計算避險張數
private static decimal CalculateHedgeQty(
    int positionQty, decimal conversionRatio, decimal delta)
    => positionQty * conversionRatio * delta;
```

---

## 4. 後端實作步驟清單

### 4.1 任務清單

#### [P0] 環境建置與專案初始化
- [ ] 任務 1：建立 .NET 8 Web API 專案（`dotnet new webapi`）（複雜度：低）
- [ ] 任務 2：安裝 NuGet 套件（Dapper、Microsoft.Data.SqlClient、Swashbuckle）（複雜度：低）
- [ ] 任務 3：設定 `appsettings.json` 連線字串（SQL Server LocalDB）（複雜度：低）
- [ ] 任務 4：設定 CORS Policy（允許前端 localhost:5173）（複雜度：低）
- [ ] 任務 5：設定 Swagger / OpenAPI 文件（複雜度：低）

#### [P0] 基礎架構層
- [ ] 任務 6：建立 `Result<T>` 通用回傳模型（複雜度：低）
- [ ] 任務 7：建立 `AppDbContext`（Dapper 連線工廠）（複雜度：低）
- [ ] 任務 8：建立 Domain Entities（`WarrantMaster`、`WarrantTrialLog`）（複雜度：低）
- [ ] 任務 9：建立 DTOs（`WarrantDto`、`TrialCalculationDto`、`SaveTrialLogRequest`、`TrialLogDto`）（複雜度：低）

#### [P1] Repository 層
- [ ] 任務 10：建立 `IWarrantRepository` 介面（複雜度：低）
- [ ] 任務 11：實作 `WarrantRepository.GetListAsync`（分頁 + 關鍵字搜尋，Set-based SQL）（複雜度：中）
- [ ] 任務 12：實作 `WarrantRepository.GetByIdAsync`（單一查詢）（複雜度：低）
- [ ] 任務 13：建立 `ITrialLogRepository` 介面（複雜度：低）
- [ ] 任務 14：實作 `TrialLogRepository.FindByIdempotencyKeyAsync`（複雜度：低）
- [ ] 任務 15：實作 `TrialLogRepository.InsertAsync`（含 Transaction）（複雜度：中）
- [ ] 任務 16：實作 `TrialLogRepository.GetRecentByWarrantIdAsync`（TOP 10，Set-based）（複雜度：低）

#### [P1] Service 層
- [ ] 任務 17：建立 `IWarrantService` 介面（複雜度：低）
- [ ] 任務 18：實作 `WarrantService.GetWarrantListAsync`（呼叫 Repository，組裝分頁 DTO）（複雜度：低）
- [ ] 任務 19：實作 `WarrantService.CalculateAsync`（純計算邏輯，含 marketPrice <= 0 防護）（複雜度：中）
- [ ] 任務 20：建立 `ITrialLogService` 介面（複雜度：低）
- [ ] 任務 21：實作 `TrialLogService.SaveAsync`（冪等性判斷 + 交易寫入 + 例外捕獲）（複雜度：高）
- [ ] 任務 22：實作 `TrialLogService.GetRecentLogsAsync`（複雜度：低）

#### [P1] Controller 層
- [ ] 任務 23：建立 `WarrantsController`（GET list、GET by id、POST calculate）（複雜度：中）
- [ ] 任務 24：建立 `TrialLogsController`（POST save、GET recent logs）（複雜度：中）
- [ ] 任務 25：加入全域例外處理 Middleware（複雜度：中）
- [ ] 任務 26：DI 容器註冊（Services、Repositories、DbContext）（複雜度：低）

#### [P2] 品質與驗證
- [ ] 任務 27：加入輸入驗證（FluentValidation 或 DataAnnotations）（複雜度：低）
- [ ] 任務 28：加入 Logging（ILogger，記錄關鍵操作與例外）（複雜度：低）
- [ ] 任務 29：Swagger 測試所有 Endpoints（複雜度：低）

### 4.2 自我審查清單

- [ ] 所有金融欄位使用 `decimal`，無 `float` / `double`
- [ ] `marketPrice <= 0` 有明確防護，回傳 400 並禁止存檔
- [ ] 資料庫寫入操作有 `try-catch` + Transaction
- [ ] 冪等性：相同 `X-Idempotency-Key` 回傳相同結果不報錯
- [ ] SQL 無 Loop 查詢（Set-based）
- [ ] 分頁查詢而非一次撈全部
- [ ] Controller 無業務邏輯
- [ ] Service 不直接操作 DbContext（透過 Repository）
- [ ] 依賴注入正確設定（Scoped）
- [ ] 無硬編碼 Magic Number（Delta 值定義於常數或設定）

---

## 5. 前端實作步驟清單

### 5.1 任務清單

#### [P0] 環境建置
- [ ] 任務 1：建立 Vue 3 + TypeScript + Vite 專案（`npm create vue@latest`）（複雜度：低）
- [ ] 任務 2：安裝依賴（Pinia、Vue Router、Axios、uuid）（複雜度：低）
- [ ] 任務 3：設定 `vite.config.ts` API Proxy（開發環境代理至後端）（複雜度：低）

#### [P0] 型別與 API 層
- [ ] 任務 4：定義 TypeScript 型別（`Warrant`、`TrialCalculation`、`TrialLog`）（複雜度：低）
- [ ] 任務 5：建立 `warrantsApi.ts`（封裝所有 Axios 呼叫，含冪等 Key 產生）（複雜度：中）

#### [P1] 狀態管理
- [ ] 任務 6：建立 Pinia Store（`warrantsStore`：儲存列表、選中權證、搜尋關鍵字）（複雜度：中）

#### [P1] 元件開發
- [ ] 任務 7：開發 `WarrantList.vue`（左側列表、虛擬滾動或分頁、關鍵字搜尋 debounce 300ms）（複雜度：高）
- [ ] 任務 8：開發 `TrialPanel.vue`（標的股價輸入、即時試算、顯示理論價值與避險張數、儲存按鈕）（複雜度：高）
- [ ] 任務 9：開發 `TrialLogTable.vue`（最近 10 筆紀錄表格）（複雜度：中）
- [ ] 任務 10：開發 Toast 通知元件（儲存成功 / 失敗提示）（複雜度：低）
- [ ] 任務 11：組合 `App.vue` 主版面（左側列表 + 右側面板）（複雜度：低）

#### [P1] 即時試算邏輯
- [ ] 任務 12：建立 `useTrialCalculation` composable（監聽 marketPrice 輸入，呼叫 calculate API）（複雜度：中）
- [ ] 任務 13：加入 debounce 防抖（避免每個按鍵都觸發 API）（複雜度：低）

#### [P2] 使用者體驗
- [ ] 任務 14：加入 Loading 狀態（列表載入、計算中、儲存中）（複雜度：低）
- [ ] 任務 15：加入錯誤狀態（API 失敗提示）（複雜度：低）
- [ ] 任務 16：RWD 基本樣式（桌面優先）（複雜度：中）

### 5.2 前端關鍵技術決策

| 決策 | 選擇 | 理由 |
|------|------|------|
| 即時試算觸發 | 呼叫後端 `/calculate`（非前端計算） | 確保計算邏輯唯一來源在後端，前後端結果一致 |
| 搜尋防抖 | 300ms debounce | 避免頻繁打 API，800 筆需搜尋 |
| 冪等 Key 產生 | `crypto.randomUUID()` 於點擊儲存時產生 | 每次點擊產生新 Key，避免重複點擊重複寫入 |
| 列表渲染 | 後端分頁（每頁 50 筆） | 800 筆前端一次渲染效能可接受，但搭配後端分頁更佳 |

---

## 6. 測試資料生成計畫

### 6.1 生成策略

800 筆 `Warrant_Master` 需涵蓋多種場景以驗證計算正確性：

| 場景 | 比例 | 說明 |
|------|------|------|
| CALL 權證 | 50%（400 筆） | Call 型態 |
| PUT 權證 | 50%（400 筆） | Put 型態 |
| 履約價範圍 | 10 ~ 1000 | 模擬台股常見股價區間 |
| 行使比例範圍 | 0.05 ~ 1.0 | 0.05 / 0.1 / 0.2 / 0.5 / 1.0 |
| 庫存張數範圍 | 100 ~ 100,000 | 整數 |

### 6.2 Warrant_ID 命名規則

```
格式：{5碼數字}{1碼英文後綴}
- 數字部分：00001 ~ 00800（左補零）
- 後綴：CALL 用 'C'，PUT 用 'P'
範例：00001C, 00002P, 00003C ...
```

### 6.3 SQL 測試資料 Script 範例（前 5 筆）

```sql
-- 03_seed_data.sql（使用 T-SQL 迴圈批次產生 800 筆）

DECLARE @i INT = 1;
DECLARE @warrantId VARCHAR(10);
DECLARE @type VARCHAR(4);
DECLARE @strike DECIMAL(18,4);
DECLARE @ratio DECIMAL(18,4);
DECLARE @qty INT;

WHILE @i <= 800
BEGIN
    SET @type = CASE WHEN @i % 2 = 1 THEN 'CALL' ELSE 'PUT' END;
    SET @warrantId = RIGHT('00000' + CAST(@i AS VARCHAR), 5)
                     + CASE WHEN @type = 'CALL' THEN 'C' ELSE 'P' END;

    -- 履約價：10 ~ 1000，每 10 遞增，超過重置
    SET @strike = CAST(10 + ((@i - 1) % 99) * 10 AS DECIMAL(18,4));

    -- 行使比例：循環使用 5 種
    SET @ratio = CASE (@i % 5)
        WHEN 0 THEN 0.0500
        WHEN 1 THEN 0.1000
        WHEN 2 THEN 0.2000
        WHEN 3 THEN 0.5000
        WHEN 4 THEN 1.0000
    END;

    -- 庫存張數：100 ~ 100000
    SET @qty = 100 + ((@i - 1) * 125) % 99901;

    INSERT INTO Warrant_Master (Warrant_ID, Strike_Price, Conversion_Ratio, Warrant_Type, Position_Qty)
    VALUES (@warrantId, @strike, @ratio, @type, @qty);

    SET @i = @i + 1;
END;
```

### 6.4 資料驗證 Script

```sql
-- 驗證 800 筆均已插入
SELECT COUNT(*) AS TotalCount FROM Warrant_Master;          -- 應為 800

-- 驗證類型分佈
SELECT Warrant_Type, COUNT(*) AS Cnt
FROM Warrant_Master
GROUP BY Warrant_Type;                                        -- CALL:400, PUT:400

-- 驗證無非法值
SELECT COUNT(*) FROM Warrant_Master WHERE Strike_Price <= 0;  -- 應為 0
SELECT COUNT(*) FROM Warrant_Master WHERE Conversion_Ratio <= 0; -- 應為 0
```

---

## 7. 風險點與應對策略

### 7.1 金融精度風險

**風險**：使用 `double` 或 `float` 計算理論價值，導致精度丟失。

**應對**：
- C# 所有計算使用 `decimal` 型別
- SQL 欄位使用 `DECIMAL(18,4)`（金額）/ `DECIMAL(18,2)`（張數）
- 任何從外部輸入的數值（Query String、JSON Body）都明確轉型為 `decimal`
- `Math.Max(0m, ...)` 防止理論價值為負

### 7.2 並發寫入衝突（Race Condition）

**風險**：使用者快速點擊「儲存」兩次，兩個請求同時抵達後端，都通過冪等 Key 查詢（查到空），同時插入造成重複。

**應對**：
- 前端：儲存按鈕點擊後立即 disabled，等 API 回應後才恢復
- 後端：捕獲 `SqlException`（錯誤碼 2627 = UNIQUE 約束違反），查詢已存結果後回傳，不向使用者拋出 500 錯誤

### 7.3 N+1 查詢風險

**風險**：在迴圈內逐筆查詢試算日誌。

**應對**：
- 明確禁止在 foreach/for 迴圈內呼叫任何 Repository 方法
- 所有批次查詢使用 `WHERE ... IN (...)` 或視窗函數（ROW_NUMBER）

### 7.4 輸入驗證繞過風險

**風險**：前端驗證被繞過（直接呼叫 API），傳入 `marketPrice <= 0`。

**應對**：
- 後端 Service 層強制驗證 `marketPrice <= 0` → 回傳 `Result.Failure`
- SQL CHECK 約束作為最後一道防線
- 不依賴前端驗證作為安全保障

### 7.5 大量資料列表效能風險

**風險**：800 筆 Warrant_Master 一次回傳，前端渲染卡頓。

**應對**：
- 後端實作分頁（`OFFSET ... FETCH NEXT ...`）
- 關鍵字搜尋使用 `LIKE @keyword + '%'`（前綴搜尋，可利用索引）而非 `LIKE '%' + @keyword + '%'`（全文掃描）
- 若需支援任意位置搜尋，可考慮 Full-Text Index

### 7.6 交易一致性風險

**風險**：寫入 `Warrant_Trial_Log` 成功後，後續操作失敗，導致部分寫入。

**應對**：
- 寫入操作包在 `SqlTransaction` 中
- `catch` 區塊執行 `transaction.Rollback()`
- `finally` 確保資源釋放

---

## 8. 實作順序建議

### 8.1 建議執行順序

```
Week 1 Day 1 — 資料庫 + 後端基礎
  ├─ [1] 執行 DDL（建表、索引）
  ├─ [2] 執行 Seed Data（800 筆）
  ├─ [3] 建立 .NET 8 Web API 專案結構
  └─ [4] 設定連線字串、DI、CORS、Swagger

Week 1 Day 2 — 後端核心
  ├─ [5] 實作 Domain Entities + DTOs
  ├─ [6] 實作 Result<T> 通用模型
  ├─ [7] 實作 WarrantRepository（Get list、Get by id）
  └─ [8] 實作 WarrantService + WarrantsController

Week 1 Day 3 — 後端試算與儲存
  ├─ [9]  實作計算邏輯（CalculateAsync）
  ├─ [10] 實作 TrialLogRepository（冪等查詢 + 插入 + 最近 10 筆）
  ├─ [11] 實作 TrialLogService（含冪等 + 交易 + 例外）
  └─ [12] 實作 TrialLogsController + 全域例外 Middleware

Week 1 Day 4-5 — 前端開發
  ├─ [13] 建立 Vue 3 專案 + API 層 + 型別定義
  ├─ [14] 開發 WarrantList.vue（列表 + 搜尋）
  ├─ [15] 開發 TrialPanel.vue（即時試算 + 儲存）
  └─ [16] 開發 TrialLogTable.vue + Toast + 整合

Week 2 Day 1 — 收尾
  ├─ [17] E2E 功能驗證（完整流程測試）
  ├─ [18] 撰寫 README.md
  └─ [19] Git 整理（分支合併、tag）
```

### 8.2 順序邏輯說明

1. **資料庫優先**：Schema 確定後，後端 Entity 才能對應，測試資料讓後續 API 可立即驗證。
2. **後端先於前端**：前端需要 API Contract 才能正確開發，等 Swagger 文件確認後再動前端。
3. **計算邏輯在儲存之前**：`/calculate` 是純計算（無副作用），先確認計算正確，再實作儲存（有副作用、冪等要求）。
4. **冪等實作在儲存時一起完成**：不可事後補做，否則需要大幅重構 Controller、Service、Repository 三層。
5. **前端分層開發**：API 層 → Store → 元件，確保資料流向清晰，元件不直接呼叫 Axios。

### 8.3 完成標準（Definition of Done）

每個任務須同時滿足以下條件才算完成：

- [ ] 功能符合需求規格
- [ ] 所有金融數值使用 `decimal`
- [ ] 例外情況有明確的錯誤回傳（非 500 Crash）
- [ ] 無 N+1 查詢（已確認 SQL Profiler / 日誌）
- [ ] 冪等操作（寫入類 API）已驗證重複請求行為
- [ ] Swagger 文件可成功呼叫
- [ ] Code Review 通過分層架構審查

---

*本計畫文件應隨實作進度持續更新，任何設計變更須同步修改本文件。*
