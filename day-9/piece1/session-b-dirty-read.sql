-- Dirty read - Session B (the reader). Run this at the same time as
-- session-a-dirty-read.sql. Phase 1 reads at READ UNCOMMITTED while A's
-- update is still uncommitted (t=2, inside A's [0,5] window) - expect to see
-- the phantom 9999.00. Phase 2 reads at READ COMMITTED instead (t=9, inside
-- A's [7,12] window) - expect this SELECT to block until A's rollback at
-- t=12, then return the real value, 100.00.
USE Day9Piece1;
GO

-- ============================================================
-- Phase 1: read at READ UNCOMMITTED during A's open (uncommitted) transaction.
-- ============================================================
WAITFOR DELAY '00:00:02';
GO
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
PRINT CONVERT(varchar, SYSDATETIME(), 121) + '  B: [phase 1, READ UNCOMMITTED] reading Balance for AccountId = 1...';
SELECT Balance FROM Accounts WHERE AccountId = 1;
PRINT CONVERT(varchar, SYSDATETIME(), 121) + '  B: [phase 1] read returned (above)';
GO

-- ============================================================
-- Phase 2: read at READ COMMITTED during A's open (uncommitted) transaction.
-- ============================================================
WAITFOR DELAY '00:00:07';
GO
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
PRINT CONVERT(varchar, SYSDATETIME(), 121) + '  B: [phase 2, READ COMMITTED] attempting read for AccountId = 1 (expect this to block)...';
SELECT Balance FROM Accounts WHERE AccountId = 1;
PRINT CONVERT(varchar, SYSDATETIME(), 121) + '  B: [phase 2] read returned (above) - only after A released its lock';
GO
