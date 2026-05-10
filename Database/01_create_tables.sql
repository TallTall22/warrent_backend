-- ============================================================
-- 01_create_tables.sql
-- 權證發行風險監控與避險試算系統 — 資料表建立腳本
-- ============================================================

-- 權證基本檔
CREATE TABLE Warrant_Master (
    Warrant_ID       VARCHAR(10)    NOT NULL,
    Strike_Price     DECIMAL(18,4)  NOT NULL,
    Conversion_Ratio DECIMAL(18,4)  NOT NULL,
    Warrant_Type     VARCHAR(4)     NOT NULL,
    Position_Qty     INT            NOT NULL,
    CONSTRAINT PK_Warrant_Master PRIMARY KEY (Warrant_ID),
    CONSTRAINT CK_Warrant_Type CHECK (Warrant_Type IN ('CALL','PUT')),
    CONSTRAINT CK_Strike_Price CHECK (Strike_Price > 0),
    CONSTRAINT CK_Conversion_Ratio CHECK (Conversion_Ratio > 0),
    CONSTRAINT CK_Position_Qty CHECK (Position_Qty >= 0)
);

-- 試算日誌表（含冪等鍵）
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
