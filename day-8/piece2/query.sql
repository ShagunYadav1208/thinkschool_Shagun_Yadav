-- Day 8 - Piece 2: Covering indexes + INCLUDEd columns
-- Target: SQL Server / Azure SQL (T-SQL). Run top-to-bottom on a scratch database -
-- this script drops and recreates it. Every number in README.md was captured by
-- actually running this exact script against SQL Server 2022 (mssql/server:2022-latest
-- in Docker; no local SQL Server instance was otherwise available), not estimated.

SET NOCOUNT ON;
IF DB_ID('Day8Piece2') IS NOT NULL
BEGIN
    ALTER DATABASE Day8Piece2 SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE Day8Piece2;
END
CREATE DATABASE Day8Piece2;
GO
USE Day8Piece2;
GO

-- ============================================================
-- Schema: clustered PK on OrderId from the start - this exercise isn't about
-- heap vs clustered (that's Piece 1), it's specifically about the key lookup
-- a non-covering non-clustered index leaves behind.
-- ============================================================

CREATE TABLE Orders (
    OrderId     INT             NOT NULL,
    CustomerId  INT             NOT NULL,
    Status      NVARCHAR(20)    NOT NULL,
    OrderTotal  DECIMAL(10,2)   NOT NULL,
    CreatedAt   DATETIME2       NOT NULL,
    CONSTRAINT PK_Orders PRIMARY KEY CLUSTERED (OrderId)
);
GO

-- ============================================================
-- Seed data: 100,000 rows, generated set-based. CustomerId cycles across
-- 2,000 values (~50 orders/customer), so CustomerId = 777 always matches
-- exactly 50 rows.
-- ============================================================

;WITH L0 AS (SELECT 1 AS c UNION ALL SELECT 1),
L1 AS (SELECT 1 AS c FROM L0 A CROSS JOIN L0 B),
L2 AS (SELECT 1 AS c FROM L1 A CROSS JOIN L1 B),
L3 AS (SELECT 1 AS c FROM L2 A CROSS JOIN L2 B),
L4 AS (SELECT 1 AS c FROM L3 A CROSS JOIN L3 B),
L5 AS (SELECT 1 AS c FROM L4 A CROSS JOIN L1 B),
Nums AS (SELECT ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM L5)
INSERT INTO Orders (OrderId, CustomerId, Status, OrderTotal, CreatedAt)
SELECT
    n                                                  AS OrderId,
    1 + (n % 2000)                                     AS CustomerId,
    CASE n % 4
        WHEN 0 THEN N'Pending'
        WHEN 1 THEN N'Shipped'
        WHEN 2 THEN N'Delivered'
        ELSE N'Cancelled'
    END                                                AS Status,
    CAST(10 + (n % 5000) / 3.0 AS DECIMAL(10,2))       AS OrderTotal,
    DATEADD(MINUTE, n, '2024-01-01T00:00:00')          AS CreatedAt
FROM Nums
WHERE n <= 100000
OPTION (MAXRECURSION 0);
GO

-- ============================================================
-- Step 1: a non-clustered index that is NOT covering for the query below -
-- it only carries CustomerId (plus OrderId, the clustering key, for free).
-- Status and OrderTotal aren't in it, so the query needs a Key Lookup.
-- ============================================================

CREATE NONCLUSTERED INDEX IX_Orders_CustomerId ON Orders(CustomerId);
GO

-- ============================================================
-- BEFORE: the query does a key lookup.
-- Real captured: 164 logical reads. Plan (via SET STATISTICS PROFILE ON,
-- no SSMS available to pull a graphical plan): Nested Loops(Inner Join)
--   |--Index Seek(IX_Orders_CustomerId, SEEK: CustomerId = 777)
--   |--Clustered Index Seek(PK_Orders, SEEK: OrderId = ... LOOKUP)
-- ============================================================

SET STATISTICS IO ON;
SET STATISTICS PROFILE ON;
SELECT OrderId, Status, OrderTotal FROM Orders WHERE CustomerId = 777;
SET STATISTICS PROFILE OFF;
GO

-- ============================================================
-- Step 2: rebuild the same index WITH (DROP_EXISTING = ON), adding
-- Status and OrderTotal as INCLUDEd columns. INCLUDE (rather than adding
-- them as key columns) keeps the index's sort key unchanged - they only
-- ride along in the leaf row, which is all a SELECT (not a seek predicate
-- or ORDER BY) needs.
-- ============================================================

CREATE NONCLUSTERED INDEX IX_Orders_CustomerId ON Orders(CustomerId)
    INCLUDE (Status, OrderTotal)
    WITH (DROP_EXISTING = ON);
GO

-- ============================================================
-- AFTER: same query, key lookup gone.
-- Real captured: 3 logical reads. Plan:
--   |--Index Seek(IX_Orders_CustomerId, SEEK: CustomerId = 777)
-- No Nested Loops, no Clustered Index Seek - the index alone answers the query.
-- ============================================================

SET STATISTICS IO ON;
SET STATISTICS PROFILE ON;
SELECT OrderId, Status, OrderTotal FROM Orders WHERE CustomerId = 777;
SET STATISTICS PROFILE OFF;
GO
