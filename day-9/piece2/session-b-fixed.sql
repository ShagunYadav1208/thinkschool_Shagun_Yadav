-- Fix - Session B. Still "transfer 10.00 from Account 2 to Account 1" - the
-- actual math is unchanged (Account 2 still ends up -10.00, Account 1 still
-- ends up +10.00) - but the UPDATE order is now reordered to touch the
-- LOWER AccountId first, matching Session A's lock order, instead of
-- matching this transfer's own "logical" direction (debit source first).
USE Day9Piece2;
GO

BEGIN TRAN;
PRINT CONVERT(varchar, SYSDATETIME(), 121) + '  B: UPDATE AccountId = 1 (locks row 1 FIRST - the lower id - even though this is the destination account)';
UPDATE Accounts SET Balance = Balance + 10.00 WHERE AccountId = 1;
WAITFOR DELAY '00:00:03';
PRINT CONVERT(varchar, SYSDATETIME(), 121) + '  B: UPDATE AccountId = 2 (locks row 2 second)';
UPDATE Accounts SET Balance = Balance - 10.00 WHERE AccountId = 2;
PRINT CONVERT(varchar, SYSDATETIME(), 121) + '  B: committing';
COMMIT TRAN;
GO
