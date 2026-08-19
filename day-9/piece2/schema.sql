-- Day 9 - Piece 2: Reproduce and resolve a deadlock
-- Target: SQL Server / Azure SQL (T-SQL). Run once to set up, then run each
-- session-a-*.sql / session-b-*.sql pair CONCURRENTLY in two separate
-- connections started at (as close as possible to) the same instant.
-- Trace flag 1222 is turned on server-wide so every deadlock's graph gets
-- written to the SQL Server error log as XML - read it back with
-- `xp_readerrorlog 0, 1, N'deadlock'` after a repro run.

IF DB_ID('Day9Piece2') IS NOT NULL
BEGIN
    ALTER DATABASE Day9Piece2 SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE Day9Piece2;
END
CREATE DATABASE Day9Piece2;
GO
USE Day9Piece2;
GO

CREATE TABLE Accounts (
    AccountId INT             NOT NULL PRIMARY KEY,
    Balance   DECIMAL(10,2)   NOT NULL
);
GO

INSERT INTO Accounts (AccountId, Balance) VALUES (1, 100.00), (2, 100.00);
GO

DBCC TRACEON(1222, -1);
GO
