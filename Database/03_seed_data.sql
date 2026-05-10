-- ============================================================
-- 03_seed_data.sql
-- 權證發行風險監控與避險試算系統 — 測試資料植入腳本
-- 產生 800 筆 Warrant_Master 測試資料
-- 必須在 01_create_tables.sql 執行完成後再執行
-- ============================================================

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

    -- 履約價：10 ~ 1000（每 10 遞增，99 種循環）
    SET @strike = CAST(10 + ((@i - 1) % 99) * 10 AS DECIMAL(18,4));

    -- 行使比例：5 種循環
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

-- 驗證
SELECT COUNT(*) AS TotalCount FROM Warrant_Master;
SELECT Warrant_Type, COUNT(*) AS Cnt FROM Warrant_Master GROUP BY Warrant_Type;
