# 資料庫設計文件

**系統**：權證發行風險監控與避險試算系統  
**資料庫**：SQL Server（WarrantDb）  
**ORM 策略**：Dapper（手寫 SQL，無 EF Core）

---

## 目錄

1. [Schema 概覽](#1-schema-概覽)
2. [資料表詳細設計](#2-資料表詳細設計)
   - [Warrant_Master（權證主檔）](#21-warrant_master權證主檔)
   - [Warrant_Trial_Log（試算日誌）](#22-warrant_trial_log試算日誌)
3. [關聯關係](#3-關聯關係)
4. [索引設計](#4-索引設計)
5. [C# Entity 對應](#5-c-entity-對應)
6. [測試資料規格](#6-測試資料規格)
7. [設計決策說明](#7-設計決策說明)
8. [執行腳本順序](#8-執行腳本順序)

---

## 1. Schema 概覽

```
Warrant_Master (主檔)
    │
    │  1 : N
    ▼
Warrant_Trial_Log (試算日誌)
```

系統由兩張資料表組成：

| 資料表 | 用途 | 資料量預估 |
|--------|------|-----------|
| `Warrant_Master` | 儲存每支權證的靜態屬性（履約價、行使比例、類型、庫存） | 中等（數百至數千筆） |
| `Warrant_Trial_Log` | 儲存每次避險試算的結果記錄，含冪等鍵防重 | 高頻寫入（每次試算一筆） |

---

## 2. 資料表詳細設計

### 2.1 Warrant_Master（權證主檔）

```sql
CREATE TABLE Warrant_Master (
    Warrant_ID       VARCHAR(10)    NOT NULL,
    Strike_Price     DECIMAL(18,4)  NOT NULL,
    Conversion_Ratio DECIMAL(18,4)  NOT NULL,
    Warrant_Type     VARCHAR(4)     NOT NULL,
    Position_Qty     INT            NOT NULL,
    CONSTRAINT PK_Warrant_Master    PRIMARY KEY (Warrant_ID),
    CONSTRAINT CK_Warrant_Type      CHECK (Warrant_Type IN ('CALL','PUT')),
    CONSTRAINT CK_Strike_Price      CHECK (Strike_Price > 0),
    CONSTRAINT CK_Conversion_Ratio  CHECK (Conversion_Ratio > 0),
    CONSTRAINT CK_Position_Qty      CHECK (Position_Qty >= 0)
);
```

#### 欄位說明

| 欄位名稱 | 型別 | 可空 | 說明 |
|----------|------|------|------|
| `Warrant_ID` | `VARCHAR(10)` | NOT NULL | **主鍵**。權證代號，格式為 5 位數字 + 類型後綴（`C`/`P`），例如 `00001C`、`00002P` |
| `Strike_Price` | `DECIMAL(18,4)` | NOT NULL | 履約價格（元）。必須 > 0 |
| `Conversion_Ratio` | `DECIMAL(18,4)` | NOT NULL | 行使比例（幾張標的換一張權證）。必須 > 0 |
| `Warrant_Type` | `VARCHAR(4)` | NOT NULL | 權證類型。限定值：`CALL`（認購）、`PUT`（認售） |
| `Position_Qty` | `INT` | NOT NULL | 發行部位庫存數量（張）。必須 >= 0 |

#### CHECK 約束

| 約束名稱 | 規則 | 目的 |
|----------|------|------|
| `CK_Warrant_Type` | `IN ('CALL','PUT')` | 防止無效的權證類型進入資料庫 |
| `CK_Strike_Price` | `> 0` | 履約價不得為負或零 |
| `CK_Conversion_Ratio` | `> 0` | 行使比例不得為負或零 |
| `CK_Position_Qty` | `>= 0` | 庫存允許為零（已售完），但不得為負 |

---

### 2.2 Warrant_Trial_Log（試算日誌）

```sql
CREATE TABLE Warrant_Trial_Log (
    Log_ID           INT              NOT NULL IDENTITY(1,1),
    Warrant_ID       VARCHAR(10)      NOT NULL,
    Market_Price     DECIMAL(18,4)    NOT NULL,
    Theory_Price     DECIMAL(18,4)    NOT NULL,
    Hedge_Qty        DECIMAL(18,2)    NOT NULL,
    Created_Time     DATETIME         NOT NULL DEFAULT GETDATE(),
    Idempotency_Key  UNIQUEIDENTIFIER NOT NULL,
    CONSTRAINT PK_Warrant_Trial_Log    PRIMARY KEY (Log_ID),
    CONSTRAINT FK_TrialLog_Warrant     FOREIGN KEY (Warrant_ID) REFERENCES Warrant_Master(Warrant_ID),
    CONSTRAINT UQ_TrialLog_Idempotency UNIQUE (Idempotency_Key),
    CONSTRAINT CK_Market_Price         CHECK (Market_Price > 0),
    CONSTRAINT CK_Theory_Price         CHECK (Theory_Price >= 0),
    CONSTRAINT CK_Hedge_Qty            CHECK (Hedge_Qty >= 0)
);
```

#### 欄位說明

| 欄位名稱 | 型別 | 可空 | 說明 |
|----------|------|------|------|
| `Log_ID` | `INT IDENTITY(1,1)` | NOT NULL | **主鍵**。自動遞增流水號 |
| `Warrant_ID` | `VARCHAR(10)` | NOT NULL | **外鍵**，對應 `Warrant_Master.Warrant_ID` |
| `Market_Price` | `DECIMAL(18,4)` | NOT NULL | 試算時輸入的標的市場價格（元）。必須 > 0 |
| `Theory_Price` | `DECIMAL(18,4)` | NOT NULL | 計算出的權證理論價值（元）。必須 >= 0 |
| `Hedge_Qty` | `DECIMAL(18,2)` | NOT NULL | 建議的避險數量（張）。必須 >= 0 |
| `Created_Time` | `DATETIME` | NOT NULL | 試算建立時間，預設 `GETDATE()`，由資料庫自動填入 |
| `Idempotency_Key` | `UNIQUEIDENTIFIER` | NOT NULL | **冪等鍵**（UUID v4），由呼叫端提供，確保同一次試算不被重複寫入 |

#### 約束說明

| 約束名稱 | 型別 | 規則 | 目的 |
|----------|------|------|------|
| `FK_TrialLog_Warrant` | FOREIGN KEY | → `Warrant_Master.Warrant_ID` | 確保日誌對應的權證存在 |
| `UQ_TrialLog_Idempotency` | UNIQUE | `Idempotency_Key` 全域唯一 | 資料庫層的冪等性最後防線（配合應用層的 Race Condition 捕捉） |
| `CK_Market_Price` | CHECK | `> 0` | 市場價格不得為負或零 |
| `CK_Theory_Price` | CHECK | `>= 0` | 理論價值允許為零（深度價外情況） |
| `CK_Hedge_Qty` | CHECK | `>= 0` | 避險張數不得為負 |

---

## 3. 關聯關係

```
Warrant_Master                    Warrant_Trial_Log
─────────────────                 ──────────────────────────────
Warrant_ID  (PK) ◄──── FK ────── Warrant_ID
Strike_Price                      Log_ID       (PK, IDENTITY)
Conversion_Ratio                  Market_Price
Warrant_Type                      Theory_Price
Position_Qty                      Hedge_Qty
                                  Created_Time (DEFAULT GETDATE())
                                  Idempotency_Key (UNIQUE)
```

- 一支權證（`Warrant_Master`）可以對應多筆試算記錄（`Warrant_Trial_Log`）。
- 刪除 `Warrant_Master` 中的記錄時，若存在相關的 `Warrant_Trial_Log`，FK 約束會阻止刪除（預設 NO ACTION）。

---

## 4. 索引設計

```sql
-- 核心查詢索引：取某支權證最新 N 筆試算記錄
CREATE INDEX IX_TrialLog_WarrantId_LogId
    ON Warrant_Trial_Log (Warrant_ID ASC, Log_ID DESC);

-- 備用索引：依時間區間查詢
CREATE INDEX IX_TrialLog_WarrantId_CreatedTime
    ON Warrant_Trial_Log (Warrant_ID ASC, Created_Time DESC);
```

### 索引詳細說明

| 索引名稱 | 資料表 | 欄位 | 用途 |
|----------|--------|------|------|
| `IX_TrialLog_WarrantId_LogId` | `Warrant_Trial_Log` | `(Warrant_ID ASC, Log_ID DESC)` | 取特定權證最新 N 筆記錄（`TOP N ... ORDER BY Log_ID DESC`），為最高頻查詢路徑 |
| `IX_TrialLog_WarrantId_CreatedTime` | `Warrant_Trial_Log` | `(Warrant_ID ASC, Created_Time DESC)` | 備用：按時間區間查詢（如：今日試算記錄） |
| `UQ_TrialLog_Idempotency`（自動） | `Warrant_Trial_Log` | `(Idempotency_Key)` | UNIQUE 約束自動建立，支援冪等鍵快速查詢 |

### 查詢模式對應

| API 端點 | 使用索引 | 查詢模式 |
|----------|----------|---------|
| `GET /api/warrants/{id}/trial-logs` | `IX_TrialLog_WarrantId_LogId` | `WHERE Warrant_ID = @id ORDER BY Log_ID DESC TOP 10` |
| `POST /api/warrants/{id}/trial-logs`（冪等查詢） | `UQ_TrialLog_Idempotency` | `WHERE Idempotency_Key = @key` |

---

## 5. C# Entity 對應

### WarrantMaster

```csharp
public sealed class WarrantMaster
{
    public string  WarrantId       { get; set; }  // Warrant_ID       VARCHAR(10)
    public decimal StrikePrice     { get; set; }  // Strike_Price     DECIMAL(18,4)
    public decimal ConversionRatio { get; set; }  // Conversion_Ratio DECIMAL(18,4)
    public string  WarrantType     { get; set; }  // Warrant_Type     VARCHAR(4)
    public int     PositionQty     { get; set; }  // Position_Qty     INT
}
```

### WarrantTrialLog

```csharp
public sealed class WarrantTrialLog
{
    public int      LogId           { get; set; }  // Log_ID           INT IDENTITY
    public string   WarrantId       { get; set; }  // Warrant_ID       VARCHAR(10)
    public decimal  MarketPrice     { get; set; }  // Market_Price     DECIMAL(18,4)
    public decimal  TheoryPrice     { get; set; }  // Theory_Price     DECIMAL(18,4)
    public decimal  HedgeQty        { get; set; }  // Hedge_Qty        DECIMAL(18,2)
    public DateTime CreatedTime     { get; set; }  // Created_Time     DATETIME
    public Guid     IdempotencyKey  { get; set; }  // Idempotency_Key  UNIQUEIDENTIFIER
}
```

> 金融數值欄位（`Strike_Price`、`Market_Price`、`Theory_Price`、`HedgeQty`）一律使用 `decimal`，避免 IEEE 754 浮點數的精度誤差。

---

## 6. 測試資料規格

測試資料由 `03_seed_data.sql` 植入，共 **800 筆** `Warrant_Master` 記錄。

| 屬性 | 規格 |
|------|------|
| 總筆數 | 800 筆 |
| `Warrant_ID` 格式 | 5 位數字 + `C`/`P` 後綴（`00001C`、`00002P`...） |
| `Warrant_Type` 分布 | CALL 400 筆（奇數序號），PUT 400 筆（偶數序號） |
| `Strike_Price` 範圍 | 10 ~ 1000 元（每 10 元遞增，循環） |
| `Conversion_Ratio` 值 | 5 種循環：`0.05`, `0.10`, `0.20`, `0.50`, `1.00` |
| `Position_Qty` 範圍 | 100 ~ 100,000 張 |
| 生成方式 | CTE + `CROSS JOIN`（Set-based，無 `WHILE` 迴圈） |

---

## 7. 設計決策說明

### 7.1 為何選用 Dapper 而非 EF Core

- 完全掌控 SQL，可針對金融查詢的 Set-based 模式精確優化
- 避免 ORM 自動產生的 N+1 查詢
- `QueryMultipleAsync` 實現單次 round-trip 同時取得 `COUNT` 與分頁資料

### 7.2 為何金融欄位全用 DECIMAL

SQL Server 的 `DECIMAL(18,4)` 和 C# 的 `decimal` 都是十進制精確表示，不會發生 IEEE 754 的浮點累積誤差，符合金融計算的精度要求。

### 7.3 冪等性機制（雙層防護）

```
請求端 (HTTP Header: X-Idempotency-Key)
    │
    ▼
應用層：先查詢 Idempotency_Key 是否存在
    │  若存在 → 回傳既有結果
    │  若不存在 → 執行 INSERT
    │
    ▼
資料庫層：UNIQUE 約束 (UQ_TrialLog_Idempotency)
    │  並發情況下的最後防線
    │  捕獲 SqlException 2627/2601 → 改為查詢既有結果
    ▼
寫入成功
```

- **應用層**：先讀後寫，大多數情況下防止重複。
- **資料庫層**：UNIQUE 約束兜底，防止並發 Race Condition 導致的重複寫入。

### 7.4 Log_ID 作為排序依據而非 Created_Time

- `Log_ID` 為自動遞增整數，排序比較效率優於 `DATETIME`
- 同一毫秒內的多筆記錄，`Log_ID` 仍能保持確定性排序
- `IX_TrialLog_WarrantId_LogId` 索引的 `Log_ID DESC` 順序與 `TOP N` 查詢直接對齊，無需額外排序

---

## 8. 執行腳本順序

初始化資料庫時，腳本必須依序執行：

```
Database/
├── 01_create_tables.sql   ← 建立資料表與約束（必須最先執行）
├── 02_create_indexes.sql  ← 建立索引（必須在 01 之後）
└── 03_seed_data.sql       ← 植入 800 筆測試資料（可選，僅開發/測試環境）
```

完整初始化指令（SQL Server Management Studio 或 sqlcmd）：

```bash
sqlcmd -S localhost,1433 -U sa -P <password> -d WarrantDb -i Database/01_create_tables.sql
sqlcmd -S localhost,1433 -U sa -P <password> -d WarrantDb -i Database/02_create_indexes.sql
sqlcmd -S localhost,1433 -U sa -P <password> -d WarrantDb -i Database/03_seed_data.sql
```
