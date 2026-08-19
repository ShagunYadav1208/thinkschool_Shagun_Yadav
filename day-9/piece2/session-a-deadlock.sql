-- Deadlock repro - Session A. Simulates "transfer 10.00 from Account 1 to
-- Account 2": locks row 1 first, then (after a delay to let B lock row 2)
-- tries to lock row 2. Run this at the same time as session-b-deadlock.sql.
USE Day9Piece2;
GO

BEGIN TRAN;
PRINT CONVERT(varchar, SYSDATETIME(), 121) + '  A: UPDATE AccountId = 1 (locks row 1)';
UPDATE Accounts SET Balance = Balance - 10.00 WHERE AccountId = 1;
WAITFOR DELAY '00:00:03';
PRINT CONVERT(varchar, SYSDATETIME(), 121) + '  A: attempting UPDATE AccountId = 2 (needs row 2)...';
UPDATE Accounts SET Balance = Balance + 10.00 WHERE AccountId = 2;
PRINT CONVERT(varchar, SYSDATETIME(), 121) + '  A: got row 2, committing';
COMMIT TRAN;
GO
