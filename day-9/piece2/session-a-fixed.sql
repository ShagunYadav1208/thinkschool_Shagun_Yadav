-- Fix - Session A. Still "transfer 10.00 from Account 1 to Account 2", and
-- already touches the lower AccountId first, so this file is unchanged in
-- spirit from session-a-deadlock.sql - only Session B's lock order changes.
USE Day9Piece2;
GO

BEGIN TRAN;
PRINT CONVERT(varchar, SYSDATETIME(), 121) + '  A: UPDATE AccountId = 1 (locks row 1, the lower id)';
UPDATE Accounts SET Balance = Balance - 10.00 WHERE AccountId = 1;
WAITFOR DELAY '00:00:03';
PRINT CONVERT(varchar, SYSDATETIME(), 121) + '  A: UPDATE AccountId = 2 (locks row 2)';
UPDATE Accounts SET Balance = Balance + 10.00 WHERE AccountId = 2;
PRINT CONVERT(varchar, SYSDATETIME(), 121) + '  A: committing';
COMMIT TRAN;
GO
