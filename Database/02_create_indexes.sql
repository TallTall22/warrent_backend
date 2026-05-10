-- ============================================================
-- 02_create_indexes.sql
-- 權證發行風險監控與避險試算系統 — 索引建立腳本
-- 必須在 01_create_tables.sql 執行完成後再執行
-- ============================================================

-- 試算日誌：按 Warrant_ID 查最新 N 筆（核心查詢模式）
CREATE INDEX IX_TrialLog_WarrantId_LogId
    ON Warrant_Trial_Log (Warrant_ID ASC, Log_ID DESC);

-- 依時間查詢（備用）
CREATE INDEX IX_TrialLog_WarrantId_CreatedTime
    ON Warrant_Trial_Log (Warrant_ID ASC, Created_Time DESC);
