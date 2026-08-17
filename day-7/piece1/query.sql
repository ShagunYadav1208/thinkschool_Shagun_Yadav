-- Day 7 - Piece 1: Joins and CTEs at depth
-- Target: SQL Server / Azure SQL (T-SQL). A fresh two-table schema is used here
-- rather than the Week-1 Quotes DB, because that DB's Quotes table stores Author
-- as a flat string column with no separate Authors table -- there's nothing to
-- join. This schema normalizes Author out into its own table so the exercise
-- actually exercises a join, not just an aggregate.

-- ============================================================
-- Schema
-- ============================================================

CREATE TABLE Authors (
    AuthorId INT IDENTITY(1,1) PRIMARY KEY,
    Name NVARCHAR(100) NOT NULL
);

CREATE TABLE Quotes (
    QuoteId INT IDENTITY(1,1) PRIMARY KEY,
    AuthorId INT NOT NULL REFERENCES Authors(AuthorId),
    QuoteText NVARCHAR(1000) NOT NULL,
    CreatedAt DATETIME2 NOT NULL
);

-- ============================================================
-- Seed data (synthetic placeholder authors/quotes for the exercise)
-- ============================================================

INSERT INTO Authors (Name) VALUES
    (N'Aria Byte'),      -- 1
    (N'Milo Query'),     -- 2
    (N'Nadia Index'),    -- 3
    (N'Theo Cache'),     -- 4
    (N'Priya Schema'),   -- 5
    (N'Owen Table'),     -- 6
    (N'Lena View'),      -- 7
    (N'Faye Cursor'),    -- 8
    (N'Ravi Trigger'),   -- 9
    (N'Sofia Null');     -- 10, deliberately given zero quotes below

INSERT INTO Quotes (AuthorId, QuoteText, CreatedAt) VALUES
    (1, N'A clean schema is a promise you keep to your future self.', '2026-01-05T09:00:00'),
    (1, N'Normalize until it hurts, denormalize until it works.',      '2026-03-14T11:30:00'),
    (1, N'The index you forgot to add is the query you cannot explain.', '2026-06-02T16:45:00'),

    (2, N'A join is just a question about how two facts relate.',     '2026-01-20T08:15:00'),
    (2, N'Cross joins are honest about the cost everyone else hides.', '2026-02-11T13:00:00'),
    (2, N'Read the query plan before you blame the database.',        '2026-05-19T10:10:00'),
    (2, N'Every slow report is a join done wrong, dressed up as a business problem.', '2026-07-30T09:40:00'),

    (3, N'A CTE names your intent; a subquery hides it.',              '2026-02-02T14:20:00'),
    (3, N'Recursive CTEs are loops that promise to terminate.',        '2026-04-08T12:00:00'),

    (4, N'Cache invalidation is a conversation, not a switch.',        '2026-03-01T17:00:00'),

    (5, N'A foreign key is a rule that outlives the code that wrote it.', '2026-01-11T09:30:00'),
    (5, N'Schema migrations are promises made in the past tense.',     '2026-04-22T15:15:00'),
    (5, N'Constraints are documentation the database actually enforces.', '2026-06-30T10:00:00'),

    (6, N'A table without a primary key is a rumor, not a record.',    '2026-02-18T08:45:00'),
    (6, N'Wide tables age like unattended gardens.',                   '2026-05-05T11:20:00'),

    (7, N'A view is a query wearing a name tag.',                      '2026-03-27T09:10:00'),

    (8, N'A cursor is a for-loop that forgot it was allowed to say no.', '2026-01-29T13:45:00'),
    (8, N'Set-based thinking is the difference between a query and a program.', '2026-04-14T16:30:00'),

    (9, N'A trigger is an opinion the schema holds about your intentions.', '2026-02-25T10:50:00'),
    (9, N'Side effects belong in the application, not in the INSERT statement.', '2026-06-16T14:05:00');

-- Sofia Null (AuthorId 10) intentionally has no rows in Quotes, to prove
-- the LEFT JOIN below keeps authors with zero quotes in the result set.

-- ============================================================
-- Exercise query: each author, their quote count, and their most-recent quote
-- One statement, one CTE, no correlated subquery in the SELECT list.
-- ============================================================

WITH AuthorQuotesRanked AS (
    SELECT
        a.AuthorId,
        a.Name                                                            AS AuthorName,
        q.QuoteText,
        q.CreatedAt,
        COUNT(q.QuoteId)   OVER (PARTITION BY a.AuthorId)                 AS QuoteCount,
        ROW_NUMBER()       OVER (PARTITION BY a.AuthorId
                                  ORDER BY q.CreatedAt DESC)               AS RecencyRank
    FROM Authors a
    LEFT JOIN Quotes q ON q.AuthorId = a.AuthorId
)
SELECT
    AuthorId,
    AuthorName,
    QuoteCount,
    QuoteText  AS MostRecentQuote,
    CreatedAt  AS MostRecentQuoteAt
FROM AuthorQuotesRanked
WHERE RecencyRank = 1
ORDER BY QuoteCount DESC, AuthorName;
