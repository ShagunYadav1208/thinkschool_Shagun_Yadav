-- Non-repeatable read - Session B (the writer). Run this at the same time as
-- session-a-non-repeatable-read.sql. Phase 1 updates and autocommits at t=2,
-- squarely inside A's first-phase transaction window [0,5] - expect this to
-- succeed immediately (READ COMMITTED doesn't hold A's read lock between
-- statements). Phase 2 attempts the same update at t=9, inside A's
-- REPEATABLE READ window [7,12] - expect this to block until A commits at t=12.
USE Day9Piece1;
GO

-- ============================================================
-- Phase 1: update+autocommit while A (READ COMMITTED) holds its transaction open.
-- ============================================================
WAITFOR DELAY '00:00:02';
GO
PRINT CONVERT(varchar, SYSDATETIME(), 121) + '  B: [phase 1] UPDATE Balance = 500.00 WHERE AccountId = 2 (autocommit)';
UPDATE Accounts SET Balance = 500.00 WHERE AccountId = 2;
PRINT CONVERT(varchar, SYSDATETIME(), 121) + '  B: [phase 1] UPDATE committed';
GO

-- ============================================================
-- Phase 2: same update, now against A's REPEATABLE READ transaction.
-- ============================================================
WAITFOR DELAY '00:00:07';
GO
PRINT CONVERT(varchar, SYSDATETIME(), 121) + '  B: [phase 2] attempting UPDATE Balance = 999.00 WHERE AccountId = 2 (expect this to block)...';
UPDATE Accounts SET Balance = 999.00 WHERE AccountId = 2;
PRINT CONVERT(varchar, SYSDATETIME(), 121) + '  B: [phase 2] UPDATE completed - only after A committed';
GO
