-- Day 9 - Piece 1: Isolation levels + the read anomalies
-- Target: SQL Server / Azure SQL (T-SQL). Run this once to set up the database,
-- then run each session-a-*.sql / session-b-*.sql pair CONCURRENTLY, in two
-- separate connections started at (as close as possible to) the same instant -
-- e.g. two terminals, or `sqlcmd ... < session-a-x.sql & sqlcmd ... < session-b-x.sql`.
-- Every WAITFOR DELAY in these scripts is deliberate: it's what makes the
-- interleaving deterministic instead of a race that may or may not reproduce.

IF DB_ID('Day9Piece1') IS NOT NULL
BEGIN
    ALTER DATABASE Day9Piece1 SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE Day9Piece1;
END
CREATE DATABASE Day9Piece1;
GO
USE Day9Piece1;
GO

CREATE TABLE Accounts (
    AccountId INT             NOT NULL PRIMARY KEY,
    Balance   DECIMAL(10,2)   NOT NULL
);
GO

INSERT INTO Accounts (AccountId, Balance) VALUES (1, 100.00), (2, 100.00), (3, 100.00);
GO
