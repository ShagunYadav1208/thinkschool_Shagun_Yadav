-- Dirty read - Session A (the writer that never commits).
-- Timeline (t=0 is when this script and session-b-dirty-read.sql are started
-- together): Phase 1 [t=0..5] leaves an uncommitted UPDATE in flight and rolls
-- it back; Phase 2 [t=7..12] repeats the exact same thing, except this time
-- Session B is reading at READ COMMITTED instead of READ UNCOMMITTED.
USE Day9Piece1;
GO

UPDATE Accounts SET Balance = 100.00 WHERE AccountId = 1;
GO

-- ============================================================
-- Phase 1: t=0 -> t=5. Session B (READ UNCOMMITTED) will read mid-transaction.
-- ============================================================
PRINT CONVERT(varchar, SYSDATETIME(), 121) + '  A: [phase 1] BEGIN TRAN; UPDATE Balance = 9999.00 WHERE AccountId = 1 (uncommitted)';
BEGIN TRAN;
UPDATE Accounts SET Balance = 9999.00 WHERE AccountId = 1;
WAITFOR DELAY '00:00:05';
PRINT CONVERT(varchar, SYSDATETIME(), 121) + '  A: [phase 1] ROLLBACK TRAN (9999.00 never really existed)';
ROLLBACK TRAN;
GO

UPDATE Accounts SET Balance = 100.00 WHERE AccountId = 1;
WAITFOR DELAY '00:00:02';
GO

-- ============================================================
-- Phase 2: t=7 -> t=12. Session B (READ COMMITTED) will try to read mid-transaction.
-- ============================================================
PRINT CONVERT(varchar, SYSDATETIME(), 121) + '  A: [phase 2] BEGIN TRAN; UPDATE Balance = 9999.00 WHERE AccountId = 1 (uncommitted)';
BEGIN TRAN;
UPDATE Accounts SET Balance = 9999.00 WHERE AccountId = 1;
WAITFOR DELAY '00:00:05';
PRINT CONVERT(varchar, SYSDATETIME(), 121) + '  A: [phase 2] ROLLBACK TRAN';
ROLLBACK TRAN;
GO
