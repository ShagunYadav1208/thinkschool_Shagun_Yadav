-- Deadlock repro - Session B. Simulates "transfer 10.00 from Account 2 to
-- Account 1": locks row 2 first, then (after the same delay) tries to lock
-- row 1. Run this at the same time as session-a-deadlock.sql. A locks 1-then-2
-- while B locks 2-then-1 - a classic circular wait once both reach their
-- second UPDATE at the same time.
USE Day9Piece2;
GO

BEGIN TRAN;
PRINT CONVERT(varchar, SYSDATETIME(), 121) + '  B: UPDATE AccountId = 2 (locks row 2)';
UPDATE Accounts SET Balance = Balance - 10.00 WHERE AccountId = 2;
WAITFOR DELAY '00:00:03';
PRINT CONVERT(varchar, SYSDATETIME(), 121) + '  B: attempting UPDATE AccountId = 1 (needs row 1)...';
UPDATE Accounts SET Balance = Balance + 10.00 WHERE AccountId = 1;
PRINT CONVERT(varchar, SYSDATETIME(), 121) + '  B: got row 1, committing';
COMMIT TRAN;
GO
