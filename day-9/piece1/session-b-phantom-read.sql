-- Phantom read - Session B (the writer). Run this at the same time as
-- session-a-phantom-read.sql. Phase 1 inserts a new matching row at t=2,
-- inside A's REPEATABLE READ window [0,5] - expect this to succeed
-- immediately (REPEATABLE READ doesn't lock the predicate's "gap", only rows
-- already read). Phase 2 attempts the same insert at t=9, inside A's
-- SERIALIZABLE window [7,12] - expect this to block until A commits at t=12.
USE Day9Piece1;
GO

-- ============================================================
-- Phase 1: insert a new row matching A's predicate while A (REPEATABLE READ)
-- holds its transaction open.
-- ============================================================
WAITFOR DELAY '00:00:02';
GO
PRINT CONVERT(varchar, SYSDATETIME(), 121) + '  B: [phase 1] INSERT AccountId = 4, Balance = 150.00 (autocommit)';
INSERT INTO Accounts (AccountId, Balance) VALUES (4, 150.00);
PRINT CONVERT(varchar, SYSDATETIME(), 121) + '  B: [phase 1] INSERT committed';
GO

-- ============================================================
-- Phase 2: same insert, now against A's SERIALIZABLE transaction.
-- ============================================================
WAITFOR DELAY '00:00:07';
GO
PRINT CONVERT(varchar, SYSDATETIME(), 121) + '  B: [phase 2] attempting INSERT AccountId = 4, Balance = 150.00 (expect this to block)...';
INSERT INTO Accounts (AccountId, Balance) VALUES (4, 150.00);
PRINT CONVERT(varchar, SYSDATETIME(), 121) + '  B: [phase 2] INSERT completed - only after A committed';
GO
