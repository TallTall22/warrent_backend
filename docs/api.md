# 權證風險監控系統 — 後端 API 文件

**Base URL**：`http://localhost:5226`  
**Content-Type**：`application/json`  
**Swagger UI**：開發模式下可直接於 `http://localhost:5226` 開啟互動式文件

---

## 目錄

1. [權證清單（分頁 + 搜尋）](#1-get-apiwarrants)
2. [取得單筆權證](#2-get-apiwarrantswarrantid)
3. [避險試算](#3-post-apiwarrantswarrantidcalculate)
4. [儲存試算記錄](#4-post-apiwarrantswarrantidtrial-logs)
5. [取得最近試算記錄](#5-get-apiwarrantswarrantidtrial-logs)
6. [資料模型](#資料模型)
7. [錯誤格式](#錯誤格式)

---

## 1. GET /api/warrants

取得權證清單，支援關鍵字前綴搜尋與分頁。

### Query Parameters

| 參數       | 型別   | 必填 | 預設 | 說明                       |
|------------|--------|------|------|----------------------------|
| `keyword`  | string | 否   | —    | 依 Warrant_ID 前綴模糊搜尋 |
| `page`     | int    | 否   | 1    | 頁碼（最小值 1）           |
| `pageSize` | int    | 否   | 50   | 每頁筆數（1–1000）         |

### 範例請求

```
GET /api/warrants?keyword=003&page=1&pageSize=20
```

### 回應 `200 OK`

```json
{
  "data": [
    {
      "warrantId": "00300C",
      "strikePrice": 150.0000,
      "conversionRatio": 1.0000,
      "warrantType": "CALL",
      "positionQty": 10000
    }
  ],
  "total": 1,
  "page": 1,
  "pageSize": 20
}
```

| 欄位       | 說明           |
|------------|----------------|
| `data`     | 當頁資料陣列   |
| `total`    | 符合條件總筆數 |
| `page`     | 當前頁碼       |
| `pageSize` | 每頁筆數       |

---

## 2. GET /api/warrants/{warrantId}

取得單筆權證主檔資料。

### Path Parameters

| 參數        | 說明           |
|-------------|----------------|
| `warrantId` | 權證代號（≤10碼）|

### 範例請求

```
GET /api/warrants/00300C
```

### 回應 `200 OK`

```json
{
  "warrantId": "00300C",
  "strikePrice": 150.0000,
  "conversionRatio": 1.0000,
  "warrantType": "CALL",
  "positionQty": 10000
}
```

### 回應 `404 Not Found`

```json
{
  "success": false,
  "message": "找不到權證代號 '00300C'"
}
```

---

## 3. POST /api/warrants/{warrantId}/calculate

依市場價格執行避險試算，**純計算，不寫入資料庫**。  
前端應先呼叫此端點取得試算結果展示給使用者，待確認後再呼叫端點 4 存檔。

### Path Parameters

| 參數        | 說明            |
|-------------|-----------------|
| `warrantId` | 權證代號（≤10碼）|

### Request Body

```json
{
  "marketPrice": 160.00
}
```

| 欄位          | 型別    | 必填 | 說明                    |
|---------------|---------|------|-------------------------|
| `marketPrice` | decimal | 是   | 標的市場價格（必須 > 0）|

### 回應 `200 OK`

以 `warrantId=00300C`（strikePrice=150, conversionRatio=1.0, positionQty=10000, CALL）、`marketPrice=160` 為例：

```json
{
  "warrantId": "00300C",
  "marketPrice": 160.00,
  "strikePrice": 150.0000,
  "conversionRatio": 1.0000,
  "warrantType": "CALL",
  "positionQty": 10000,
  "delta": 0.8,
  "deltaStatus": "ITM",
  "theoryPrice": 10.0000,
  "hedgeQty": 8000.00
}
```

> 計算說明（以上例為準）：  
> - `deltaStatus` = ITM（160 > 150，CALL 價內）→ `delta` = **0.8**  
> - `theoryPrice` = Max(0, (160 − 150) × 1.0) = **10.0000**  
> - `hedgeQty` = 10000 × 1.0 × 0.8 = **8000.00**

| 欄位            | 說明                                                                                       |
|-----------------|--------------------------------------------------------------------------------------------|
| `delta`         | Delta 值：ITM = 0.8 / ATM = 0.5 / OTM = 0.2                                               |
| `deltaStatus`   | Delta 狀態：`ITM`（價內）/ `ATM`（平價）/ `OTM`（價外）                                    |
| `theoryPrice`   | CALL：Max(0, (市價 − 履約價) × 行使比例)；PUT：Max(0, (履約價 − 市價) × 行使比例)          |
| `hedgeQty`      | 建議避險數量 = positionQty × conversionRatio × delta                                       |

#### Delta 判斷規則

**CALL**

| 市場價 vs 履約價           | 狀態 | Delta |
|---------------------------|------|-------|
| marketPrice > strikePrice  | ITM  | 0.8   |
| marketPrice = strikePrice  | ATM  | 0.5   |
| marketPrice < strikePrice  | OTM  | 0.2   |

**PUT**（反向判斷）

| 市場價 vs 履約價           | 狀態 | Delta |
|---------------------------|------|-------|
| marketPrice < strikePrice  | ITM  | 0.8   |
| marketPrice = strikePrice  | ATM  | 0.5   |
| marketPrice > strikePrice  | OTM  | 0.2   |

### 回應 `400 Bad Request`

```json
{
  "success": false,
  "message": "找不到指定的權證"
}
```

---

## 4. POST /api/warrants/{warrantId}/trial-logs

將試算結果儲存至資料庫，**支援冪等保護**：同一個 `X-Idempotency-Key` 重複送出時，回傳已儲存的記錄而不重複寫入。

> **重要**：`theoryPrice` 與 `hedgeQty` **不由客戶端傳入**，伺服器會依 `marketPrice` 與資料庫中的權證主檔重新計算後寫入，確保資料完整性。

### Path Parameters

| 參數        | 說明            |
|-------------|-----------------|
| `warrantId` | 權證代號（≤10碼）|

### Headers

| Header              | 必填 | 說明                                         |
|---------------------|------|----------------------------------------------|
| `X-Idempotency-Key` | 是   | UUID v4 格式（如 `crypto.randomUUID()`），每次新試算產生一個新的 |

### Request Body

```json
{
  "marketPrice": 160.00
}
```

| 欄位          | 型別    | 必填 | 說明                    |
|---------------|---------|------|-------------------------|
| `marketPrice` | decimal | 是   | 標的市場價格（必須 > 0）|

### 回應 `201 Created`（新寫入）

以同前例（00300C, strikePrice=150, conversionRatio=1.0, CALL, marketPrice=160）為例，伺服器重算後回傳：

```json
{
  "logId": 42,
  "warrantId": "00300C",
  "marketPrice": 160.00,
  "theoryPrice": 10.0000,
  "hedgeQty": 8000.00,
  "createdTime": "2026-05-10T18:30:00"
}
```

### 回應 `200 OK`（冪等重複，已存在）

回傳格式與 `201` 相同，內容為原始寫入時的記錄。

> 前端可依 HTTP Status Code 區分「新建立」（201）vs「重複送出」（200）。

### 回應 `400 Bad Request`

```json
{
  "success": false,
  "message": "缺少必要的 Header：X-Idempotency-Key"
}
```

其他 400 情境：`X-Idempotency-Key` 非 UUID 格式、`marketPrice ≤ 0`、`warrantId` 不存在。

### 冪等 Key 使用建議

```js
// 每次使用者按下「儲存」按鈕時產生新的 key
const idempotencyKey = crypto.randomUUID();

// 網路錯誤重試時使用同一個 key，不會重複寫入
await fetch(`/api/warrants/${warrantId}/trial-logs`, {
  method: 'POST',
  headers: {
    'Content-Type': 'application/json',
    'X-Idempotency-Key': idempotencyKey,
  },
  body: JSON.stringify({ marketPrice }),
});
```

---

## 5. GET /api/warrants/{warrantId}/trial-logs

取得指定權證的最近 **10 筆**試算記錄，依 Log_ID 降冪排列（最新在最前）。

### Path Parameters

| 參數        | 說明            |
|-------------|-----------------|
| `warrantId` | 權證代號（≤10碼）|

### 範例請求

```
GET /api/warrants/00300C/trial-logs
```

### 回應 `200 OK`

以 00300C（strikePrice=150, conversionRatio=1.0, positionQty=10000, CALL）的兩筆記錄為例：

```json
{
  "warrantId": "00300C",
  "logs": [
    {
      "logId": 42,
      "warrantId": "00300C",
      "marketPrice": 160.00,
      "theoryPrice": 10.0000,
      "hedgeQty": 8000.00,
      "createdTime": "2026-05-10T18:30:00"
    },
    {
      "logId": 38,
      "warrantId": "00300C",
      "marketPrice": 148.00,
      "theoryPrice": 0.0000,
      "hedgeQty": 2000.00,
      "createdTime": "2026-05-10T17:10:00"
    }
  ]
}
```

> 範例計算：  
> - logId 42：160 > 150，CALL ITM，delta=0.8，`theoryPrice=(160−150)×1=10.0000`，`hedgeQty=10000×1×0.8=8000.00`  
> - logId 38：148 < 150，CALL OTM，delta=0.2，`theoryPrice=Max(0,(148−150)×1)=0.0000`，`hedgeQty=10000×1×0.2=2000.00`

> `createdTime` 為台灣時間（UTC+8），格式 `yyyy-MM-ddTHH:mm:ss`，無時區後綴。

---

## 資料模型

### WarrantDto

| 欄位              | 型別    | 說明                    |
|-------------------|---------|-------------------------|
| `warrantId`       | string  | 權證代號（最長 10 碼）  |
| `strikePrice`     | decimal | 履約價                  |
| `conversionRatio` | decimal | 行使比例                |
| `warrantType`     | string  | 類型：`CALL` 或 `PUT`   |
| `positionQty`     | int     | 發行部位庫存張數        |

### TrialCalculationDto（試算結果）

| 欄位              | 型別    | 說明                              |
|-------------------|---------|-----------------------------------|
| `warrantId`       | string  | 權證代號                          |
| `marketPrice`     | decimal | 輸入的標的市場價格                |
| `strikePrice`     | decimal | 履約價（來自主檔）                |
| `conversionRatio` | decimal | 行使比例（來自主檔）              |
| `warrantType`     | string  | `CALL` 或 `PUT`                   |
| `positionQty`     | int     | 發行部位庫存張數                  |
| `delta`           | decimal | 0.8（ITM）/ 0.5（ATM）/ 0.2（OTM）|
| `deltaStatus`     | string  | `ITM` / `ATM` / `OTM`            |
| `theoryPrice`     | decimal | 理論價值（decimal，不為負數）     |
| `hedgeQty`        | decimal | 建議避險張數                      |

### TrialLogDto（試算記錄）

| 欄位          | 型別     | 說明                                     |
|---------------|----------|------------------------------------------|
| `logId`       | int      | 記錄流水號                               |
| `warrantId`   | string   | 權證代號                                 |
| `marketPrice` | decimal  | 試算時的標的市場價格                     |
| `theoryPrice` | decimal  | 伺服器計算的理論價值                     |
| `hedgeQty`    | decimal  | 伺服器計算的建議避險張數                 |
| `createdTime` | datetime | 建立時間（台灣時間 UTC+8，無時區後綴）   |

---

## 錯誤格式

所有 `4xx` / `5xx` 錯誤均回傳統一格式：

```json
{
  "success": false,
  "message": "錯誤說明文字"
}
```

| Status | 情境                                               |
|--------|----------------------------------------------------|
| 400    | 參數驗證失敗、找不到權證、Key 格式錯誤、價格 ≤ 0  |
| 404    | `warrantId` 不存在（僅 GET /api/warrants/{id}）    |
| 500    | 伺服器內部錯誤（不洩漏 stack trace）               |
