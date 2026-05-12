-- ============================================================
-- 03_seed_data.sql
-- 權證發行風險監控與避險試算系統 — 測試資料植入腳本
-- 產生 800 筆 Warrant_Master 測試資料
-- 使用 CTE + CROSS JOIN Set-based 方式，無 WHILE 迴圈
-- 必須在 01_create_tables.sql 執行完成後再執行
-- ============================================================

WITH
Digits AS (
    SELECT 0 AS d UNION ALL SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3 UNION ALL SELECT 4
    UNION ALL SELECT 5 UNION ALL SELECT 6 UNION ALL SELECT 7 UNION ALL SELECT 8 UNION ALL SELECT 9
),
Nums AS (
    SELECT TOP 800 ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
    FROM Digits d1
    CROSS JOIN Digits d2
    CROSS JOIN Digits d3
)
INSERT INTO Warrant_Master (Warrant_ID, Strike_Price, Conversion_Ratio, Warrant_Type, Position_Qty)
SELECT
    RIGHT('00000' + CAST(n AS VARCHAR(10)), 5)
        + CASE WHEN n % 2 = 1 THEN 'C' ELSE 'P' END       AS Warrant_ID,
    CAST(10 + ((n - 1) % 99) * 10 AS DECIMAL(18,4))        AS Strike_Price,
    CAST(
        CASE n % 5
            WHEN 0 THEN 0.0500
            WHEN 1 THEN 0.1000
            WHEN 2 THEN 0.2000
            WHEN 3 THEN 0.5000
            ELSE         1.0000
        END
    AS DECIMAL(18,4))                                       AS Conversion_Ratio,
    CASE WHEN n % 2 = 1 THEN 'CALL' ELSE 'PUT' END         AS Warrant_Type,
    100 + ((n - 1) * 125) % 99901                          AS Position_Qty
FROM Nums;

-- 驗證
SELECT COUNT(*) AS TotalCount FROM Warrant_Master;
SELECT Warrant_Type, COUNT(*) AS Cnt FROM Warrant_Master GROUP BY Warrant_Type;
