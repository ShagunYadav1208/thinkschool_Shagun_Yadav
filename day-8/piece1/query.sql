-- Day 8 - Piece 1: Clustered vs non-clustered indexes
-- Target: SQL Server / Azure SQL (T-SQL). Run top-to-bottom on a scratch database -
-- this script drops and recreates it. Every number quoted in README.md was captured
-- by actually running this exact script against SQL Server 2022 (mssql/server:2022-latest
-- in Docker; no local SQL Server instance was otherwise available), not estimated.

SET NOCOUNT ON;
IF DB_ID('Day8Piece1') IS NOT NULL
BEGIN
    ALTER DATABASE Day8Piece1 SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE Day8Piece1;
END
CREATE DATABASE Day8Piece1;
GO
USE Day8Piece1;
GO

-- ============================================================
-- Schema: a heap, deliberately. No PRIMARY KEY / clustered index yet -
-- that gets added as Step 1 below, so its effect is visible as a before/after.
-- ============================================================

CREATE TABLE Orders (
    OrderId     INT             NOT NULL,
    CustomerId  INT             NOT NULL,
    Status      NVARCHAR(20)    NOT NULL,
    OrderTotal  DECIMAL(10,2)   NOT NULL,
    CreatedAt   DATETIME2       NOT NULL
);
GO

-- ============================================================
-- Seed data: 100,000 rows, generated set-based (no client round-trips).
-- CustomerId cycles across 2000 values (~50 orders/customer), Status cycles
-- across 4 values, CreatedAt is deterministic (OrderId'th minute after
-- 2024-01-01) so the range query below always matches the same 10,080 rows.
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
-- Step 0: baseline - heap, zero indexes. Same three queries this exercise
-- tracks throughout. Real captured reads: Q1 = 670, Q2 = 670, Q3 = 670
-- (a heap scan reads every page regardless of the predicate).
-- ============================================================

SET STATISTICS IO ON;

-- Q1: point lookup, will become the clustered-index case
SELECT * FROM Orders WHERE OrderId = 54321;

-- Q2: equality on a non-key column, will become the first non-clustered-index case
SELECT OrderId, Status, OrderTotal FROM Orders WHERE CustomerId = 777;

-- Q3: range scan, will become the covering non-clustered-index case
SELECT OrderId, CustomerId FROM Orders
WHERE CreatedAt >= '2024-02-01T00:00:00' AND CreatedAt < '2024-02-08T00:00:00';
GO

-- ============================================================
-- Step 1: clustered index on the primary key.
-- Real captured reads after this: Q1 = 3 (Clustered Index Seek),
-- Q2 = 676, Q3 = 676 (Clustered Index Scan - unchanged, since the
-- clustering key isn't in either predicate).
-- ============================================================

ALTER TABLE Orders ADD CONSTRAINT PK_Orders PRIMARY KEY CLUSTERED (OrderId);
GO

SET STATISTICS IO ON;
SELECT * FROM Orders WHERE OrderId = 54321;
SELECT OrderId, Status, OrderTotal FROM Orders WHERE CustomerId = 777;
SELECT OrderId, CustomerId FROM Orders
WHERE CreatedAt >= '2024-02-01T00:00:00' AND CreatedAt < '2024-02-08T00:00:00';
GO

-- ============================================================
-- Step 2: non-clustered index #1, on the equality predicate (CustomerId).
-- Not covering - Status/OrderTotal still require a key lookup back into the
-- clustered index. Real captured reads: Q2 = 676 -> 164
-- (Index Seek on IX_Orders_CustomerId + Clustered Index Seek "LOOKUP", 50 rows).
-- ============================================================

CREATE NONCLUSTERED INDEX IX_Orders_CustomerId ON Orders(CustomerId);
GO

SET STATISTICS IO ON;
SELECT OrderId, Status, OrderTotal FROM Orders WHERE CustomerId = 777;
GO

-- ============================================================
-- Step 3: non-clustered index #2, covering the range query (CreatedAt,
-- INCLUDE CustomerId - OrderId is already carried for free as the clustering
-- key on every non-clustered index row). Real captured reads: Q3 = 676 -> 30
-- (single Index Seek, no key lookup at all).
-- ============================================================

CREATE NONCLUSTERED INDEX IX_Orders_CreatedAt ON Orders(CreatedAt) INCLUDE (CustomerId);
GO

SET STATISTICS IO ON;
SELECT OrderId, CustomerId FROM Orders
WHERE CreatedAt >= '2024-02-01T00:00:00' AND CreatedAt < '2024-02-08T00:00:00';
GO

-- ============================================================
-- Write-side cost: insert the same 5,000-row batch into a heap copy (zero
-- indexes) versus a copy carrying the clustered PK + both non-clustered
-- indexes above, and compare SET STATISTICS IO's logical reads on the
-- INSERT itself. Two throwaway copies, not the live Orders table, so this
-- doesn't disturb the read numbers captured above.
-- ============================================================

SELECT OrderId, CustomerId, Status, OrderTotal, CreatedAt INTO OrdersHeapWriteTest FROM Orders WHERE OrderId <= 100000;

SELECT OrderId, CustomerId, Status, OrderTotal, CreatedAt INTO OrdersIndexedWriteTest FROM Orders WHERE OrderId <= 100000;
ALTER TABLE OrdersIndexedWriteTest ADD CONSTRAINT PK_OrdersIndexedWriteTest PRIMARY KEY CLUSTERED (OrderId);
CREATE NONCLUSTERED INDEX IX_OIWT_CustomerId ON OrdersIndexedWriteTest(CustomerId);
CREATE NONCLUSTERED INDEX IX_OIWT_CreatedAt ON OrdersIndexedWriteTest(CreatedAt) INCLUDE (CustomerId);
GO

SET STATISTICS IO ON;
SET STATISTICS TIME ON;

-- Real captured: 5,034 logical reads, 17 ms CPU / 21 ms elapsed
INSERT INTO OrdersHeapWriteTest (OrderId, CustomerId, Status, OrderTotal, CreatedAt)
SELECT 200000 + n, 1 + (n % 2000), N'Pending', CAST(10 + (n % 5000) / 3.0 AS DECIMAL(10,2)), DATEADD(MINUTE, 200000 + n, '2024-01-01T00:00:00')
FROM (SELECT TOP (5000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_objects a CROSS JOIN sys.all_objects b) x;

-- Real captured: 38,495 logical reads on the table + 10,144 on a Worktable
-- (index-maintenance spool) = 48,639 total, 35 ms CPU / 39 ms elapsed
INSERT INTO OrdersIndexedWriteTest (OrderId, CustomerId, Status, OrderTotal, CreatedAt)
SELECT 200000 + n, 1 + (n % 2000), N'Pending', CAST(10 + (n % 5000) / 3.0 AS DECIMAL(10,2)), DATEADD(MINUTE, 200000 + n, '2024-01-01T00:00:00')
FROM (SELECT TOP (5000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_objects a CROSS JOIN sys.all_objects b) x;

SET STATISTICS TIME OFF;
GO

DROP TABLE OrdersHeapWriteTest;
DROP TABLE OrdersIndexedWriteTest;
GO
