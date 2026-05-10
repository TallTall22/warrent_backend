【測驗規範與提交須知】
提交內容:請提供 Git Repository 連結,內容須包含完整原始碼 (Vue.js, C#, SQL
Script) 及 README.md。
技術要求:Frontend: Vue.js / Backend: C# (.NET Core 或 Framework) / DB: SQL
Server。
開發重點:金融數值處理精度 (Decimal)、API 冪等性、SQL 效能優化 (Set-based)、
UI 易用性。

全端開發工程師實作測驗說明書 (v4)
壹、 測驗規範
題目選擇：請由以下三個題目中任選一題完成即可。
提交要求：請於面試前完成並提交 Git Repository 連結。內容需包含：
完整原始碼 (Frontend & Backend)。
SQL 資料庫建表與測試資料 Script。
README.md 文件（包含執行說明、架構設計理由）。
技術棧要求：
Frontend: Vue.js (3.x)
Backend: C# (.NET Core 或 .NET Framework)
Database: 都可，SQL Server 尤佳
重點指標：金融數值處理的精確度、API 冪等性防護、SQL Set-based 效能優化、UI 介面易用性。

題目一：權證發行風險監控與避險試算系統
1. 故事背景
公司權證發行部門需監控標的股價對權證理論價值的影響。當股價變動時，系統須即時試算交易員應在現貨市場買入或賣出的避險張數（Delta Hedging），以維持風險中性。
2. 資料庫結構 (Table Schema)

#### A. 權證基本檔 (Warrant_Master)

| 欄位 | 型態 | 說明 |
|------|------|------|
| Warrant_ID | VARCHAR(10) | PK, 權證代碼 (如: 03001P) |
| Strike_Price | DECIMAL(18,4) | 履約價格 (執行價) |
| Conversion_Ratio | DECIMAL(18,4) | 行使比例 (例: 0.1 代表 10 換 1) |
| Warrant_Type | VARCHAR(4) | 類型 (CALL / PUT) |
| Position_Qty | INT | 庫存張數 (發行商持有部位) |

#### B. 試算日誌表 (Warrant_Trial_Log)

| 欄位 | 型態 | 說明 |
|------|------|------|
| Log_ID | INT (Identity) | PK, 自動編號 |
| Warrant_ID | VARCHAR(10) | 關聯之權證代碼 |
| Market_Price | DECIMAL(18,4) | 試算時輸入的標的股價 |
| Theory_Price | DECIMAL(18,4) | 計算出之理論價值 |
| Hedge_Qty | DECIMAL(18,2) | 建議避險張數 |
| Created_Time | DATETIME | 存檔時間 |

3. 核心計算邏輯 (C# API)

 理論價值計算：
   - CALL: Max(0, (標的價 - 履約價) * 行使比例)
   - PUT: Max(0, (履約價 - 標的價) * 行使比例)

避險張數試算 (Delta 簡化模型)：
   - 公式：避險張數 = 庫存張數 * 行使比例 * Delta
   - Delta 定義：
     - 價內 (ITM): 標的價 > 履約價(Call) 或 標的價 < 履約價(Put) -> Delta = 0.8
     - 價平 (ATM): 標的價 = 履約價 -> Delta = 0.5
     - 價外 (OTM): 其餘狀況 -> Delta = 0.2

 防禦性檢查：
   - 標的價輸入 <= 0 時，API 需回傳錯誤，禁止存檔。
4. UI 畫面詳細描述 (Vue.js)
權證導覽列：左側顯示 800 筆測試資料列表，支援代碼關鍵字搜尋。
動態試算面板：
輸入 [標的股價] 時，畫面需即時反映 [理論價值] 與 [建議避險張數]。
提供 [儲存試算結果] 按鈕，點擊後寫入 Warrant_Trial_Log 並跳出 Toast 成功提示。
歷史明細區：底部表格列出該檔權證最近 10 筆存檔紀錄，包含時間與計算結果。

肆、 交付產出與評選指標
提交內容：
Git Repository (完整原始碼)。
SQL 建表與測試資料 Script。
README 文件：內含架構說明、金融數值精確度處理說明、AI 工具 (如 VibeCoding) 使用與審核報告。
評選重點：
是否有使用 decimal 型別處理金額？
SQL 查詢是否具備效能意識（無 Loop 查詢）？
是否具備錯誤處理機制 (Try-Catch, Transaction)？
README 是否清晰且能主動回報潛在問題。
