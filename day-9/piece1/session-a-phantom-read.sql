-- Phantom read - Session A (the reader, same transaction, two range counts).
-- Phase 1 [t=0..5] counts at REPEATABLE READ - Session B's committed INSERT
-- of a new row matching the predicate should still show up in the second
-- count (REPEATABLE READ locks the rows it already read, not the gap).
-- Phase 2 [t=7..12] counts at SERIALIZABLE instead - Session B's INSERT
-- should now block until this transaction commits, so the second count
-- matches the first.
USE Day9Piece1;
GO

DELETE FROM Accounts WHERE AccountId = 4;
GO

-- ============================================================
-- Phase 1: t=0 -> t=5, REPEATABLE READ.
-- ============================================================
SET TRANSACTION ISOLATION LEVEL REPEATABLE READ;
BEGIN TRAN;
PRINT CONVERT(varchar, SYSDATETIME(), 121) + '  A: [phase 1, REPEATABLE READ] first count WHERE Balance >= 100.00';
SELECT COUNT(*) AS Total FROM Accounts WHERE Balance >= 100.00;
WAITFOR DELAY '00:00:05';
PRINT CONVERT(varchar, SYSDATETIME(), 121) + '  A: [phase 1] second count, same transaction, same predicate';
SELECT COUNT(*) AS Total FROM Accounts WHERE Balance >= 100.00;
COMMIT TRAN;
GO

DELETE FROM Accounts WHERE AccountId = 4;
WAITFOR DELAY '00:00:02';
GO

-- ============================================================
-- Phase 2: t=7 -> t=12, SERIALIZABLE.
-- ============================================================
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRAN;
PRINT CONVERT(varchar, SYSDATETIME(), 121) + '  A: [phase 2, SERIALIZABLE] first count WHERE Balance >= 100.00';
SELECT COUNT(*) AS Total FROM Accounts WHERE Balance >= 100.00;
WAITFOR DELAY '00:00:05';
PRINT CONVERT(varchar, SYSDATETIME(), 121) + '  A: [phase 2] second count, same transaction';
SELECT COUNT(*) AS Total FROM Accounts WHERE Balance >= 100.00;
COMMIT TRAN;
GO
