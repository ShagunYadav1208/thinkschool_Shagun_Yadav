-- Non-repeatable read - Session A (the reader, same transaction, two reads).
-- Phase 1 [t=0..5] reads at READ COMMITTED - Session B's committed UPDATE in
-- between should change the second read. Phase 2 [t=7..12] reads at
-- REPEATABLE READ instead - Session B's UPDATE should now block until this
-- transaction commits, so the second read matches the first.
USE Day9Piece1;
GO

UPDATE Accounts SET Balance = 100.00 WHERE AccountId = 2;
GO

-- ============================================================
-- Phase 1: t=0 -> t=5, READ COMMITTED.
-- ============================================================
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
BEGIN TRAN;
PRINT CONVERT(varchar, SYSDATETIME(), 121) + '  A: [phase 1, READ COMMITTED] first read of Balance for AccountId = 2';
SELECT Balance FROM Accounts WHERE AccountId = 2;
WAITFOR DELAY '00:00:05';
PRINT CONVERT(varchar, SYSDATETIME(), 121) + '  A: [phase 1] second read, same transaction, no write of my own in between';
SELECT Balance FROM Accounts WHERE AccountId = 2;
COMMIT TRAN;
GO

UPDATE Accounts SET Balance = 100.00 WHERE AccountId = 2;
WAITFOR DELAY '00:00:02';
GO

-- ============================================================
-- Phase 2: t=7 -> t=12, REPEATABLE READ.
-- ============================================================
SET TRANSACTION ISOLATION LEVEL REPEATABLE READ;
BEGIN TRAN;
PRINT CONVERT(varchar, SYSDATETIME(), 121) + '  A: [phase 2, REPEATABLE READ] first read of Balance for AccountId = 2';
SELECT Balance FROM Accounts WHERE AccountId = 2;
WAITFOR DELAY '00:00:05';
PRINT CONVERT(varchar, SYSDATETIME(), 121) + '  A: [phase 2] second read, same transaction';
SELECT Balance FROM Accounts WHERE AccountId = 2;
COMMIT TRAN;
GO
