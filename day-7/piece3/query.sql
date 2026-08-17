-- Day 7 - Piece 3: Set operations from a spec
-- Target: SQL Server / Azure SQL (T-SQL).
--
-- Authors and Quotes are copied from day-7/piece1's schema and seed data
-- unchanged (piece1's own file is left untouched). Tags and AuthorTags are
-- new tables added for this exercise, since answering "authors with no tags"
-- or "tag list across categories" needs something to tag with.

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

CREATE TABLE Tags (
    TagId INT IDENTITY(1,1) PRIMARY KEY,
    TagName NVARCHAR(50) NOT NULL,
    Category NVARCHAR(50) NOT NULL
    -- TagName is deliberately NOT unique on its own: 'classic' exists once
    -- under Category = 'Era' and again under Category = 'Style' (two
    -- different TagIds), so the Piece 3 UNION query has a real duplicate
    -- name to collapse, not just two already-disjoint lists.
);

CREATE TABLE AuthorTags (
    AuthorId INT NOT NULL REFERENCES Authors(AuthorId),
    TagId INT NOT NULL REFERENCES Tags(TagId),
    PRIMARY KEY (AuthorId, TagId)
);

-- ============================================================
-- Seed data: Authors + Quotes (synthetic placeholder authors/quotes,
-- identical to day-7/piece1 and day-7/piece2)
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

-- Sofia Null (AuthorId 10) has zero quotes.

-- ============================================================
-- Seed data: Tags + AuthorTags
-- ============================================================

INSERT INTO Tags (TagName, Category) VALUES
    (N'classic',      N'Era'),    -- 1
    (N'modern',       N'Era'),    -- 2
    (N'contemporary', N'Era'),    -- 3
    (N'stoic',        N'Theme'),  -- 4
    (N'satirical',    N'Theme'),  -- 5
    (N'minimalist',   N'Theme'),  -- 6
    (N'aphorism',     N'Style'),  -- 7
    (N'epigram',      N'Style'),  -- 8
    (N'classic',      N'Style');  -- 9, same name as tag 1, different category

INSERT INTO AuthorTags (AuthorId, TagId) VALUES
    (1, 1),  -- Aria Byte: classic (Era)
    (1, 4),  -- Aria Byte: stoic
    (2, 2),  -- Milo Query: modern (Era)
    (2, 5),  -- Milo Query: satirical
    (3, 1),  -- Nadia Index: classic (Era)
    (3, 2),  -- Nadia Index: modern (Era)  -> in both Era sets
    (5, 2),  -- Priya Schema: modern (Era)
    (5, 6),  -- Priya Schema: minimalist
    (6, 1),  -- Owen Table: classic (Era)
    (8, 7),  -- Faye Cursor: aphorism
    (8, 9),  -- Faye Cursor: classic (Style)
    (9, 8),  -- Ravi Trigger: epigram
    (9, 2),  -- Ravi Trigger: modern (Era)
    (10, 1); -- Sofia Null: classic (Era) -- but she has no quotes at all

-- Theo Cache (4) and Lena View (7) intentionally get no tags at all, even
-- though both have quotes -- they're the expected answer to Question 1.

-- ============================================================
-- Question 1: authors with quotes but no tags -> EXCEPT
--
-- "Authors with at least one quote" and "authors with at least one tag" are
-- each naturally a set of AuthorIds; EXCEPT is the direct translation of
-- "in the first set, remove anyone who's in the second."
-- ============================================================

SELECT a.AuthorId, a.Name
FROM Authors a
JOIN Quotes q ON q.AuthorId = a.AuthorId

EXCEPT

SELECT a.AuthorId, a.Name
FROM Authors a
JOIN AuthorTags at ON at.AuthorId = a.AuthorId

ORDER BY 1;
-- ORDER BY references the output column by position, not by table-qualified
-- name (AuthorId is ambiguous here -- both Authors and Quotes/AuthorTags
-- have a column with that name, and a compound query's ORDER BY only sees
-- the final result set's column list, not the underlying joins).

-- ============================================================
-- Question 2: authors in both the 'classic' and 'modern' sets -> INTERSECT
--
-- Each side is "the set of authors tagged with this one Era tag." Being "in
-- both" sets is literally set intersection, so INTERSECT is the direct
-- translation -- no CASE/COUNT gymnastics needed to express "has both."
-- ============================================================

SELECT a.AuthorId, a.Name
FROM Authors a
JOIN AuthorTags at ON at.AuthorId = a.AuthorId
JOIN Tags t ON t.TagId = at.TagId
WHERE t.Category = N'Era' AND t.TagName = N'classic'

INTERSECT

SELECT a.AuthorId, a.Name
FROM Authors a
JOIN AuthorTags at ON at.AuthorId = a.AuthorId
JOIN Tags t ON t.TagId = at.TagId
WHERE t.Category = N'Era' AND t.TagName = N'modern'

ORDER BY 1;

-- ============================================================
-- Question 3: combined distinct tag list across two categories -> UNION
--
-- 'classic' exists as both an Era tag and a Style tag (two different
-- TagIds). A plain UNION ALL or string-concat would show it twice; UNION's
-- built-in DISTINCT is exactly "combined *distinct* tag list" from the spec.
-- ============================================================

SELECT TagName
FROM Tags
WHERE Category = N'Era'

UNION

SELECT TagName
FROM Tags
WHERE Category = N'Style'

ORDER BY 1;
