# Day 7 - Piece 3: Set operations from a spec

`Authors` and `Quotes` are copied from [day-7/piece1](../piece1) unchanged (piece1's own file was
left untouched). Two new tables are added because the three business questions need something to
tag with: `Tags` (`TagId`, `TagName`, `Category`) and `AuthorTags` (the many-to-many link between
authors and tags). `TagName` is deliberately **not** unique on its own — `'classic'` exists twice,
once under `Category = 'Era'` and once under `Category = 'Style'` — specifically so Question 3's
`UNION` has a real duplicate to collapse, not just two already-disjoint lists.

Full schema, seed data, and all three queries are in [query.sql](query.sql).

## Question 1 — authors with quotes but no tags → `EXCEPT`

"Authors with at least one quote" and "authors with at least one tag" are each naturally a set of
`AuthorId`s. "Has quotes but not tags" is directly *the first set, minus anyone in the second* —
that's what `EXCEPT` means, so it's the literal translation, not a `LEFT JOIN ... WHERE IS NULL`
workaround.

```sql
SELECT a.AuthorId, a.Name
FROM Authors a
JOIN Quotes q ON q.AuthorId = a.AuthorId

EXCEPT

SELECT a.AuthorId, a.Name
FROM Authors a
JOIN AuthorTags at ON at.AuthorId = a.AuthorId

ORDER BY 1;
```

| AuthorId | Name       |
|----------|------------|
| 4        | Theo Cache |
| 7        | Lena View  |

Both have exactly one quote in the seed data and were deliberately left untagged, so this is the
expected, verified result — not everyone with quotes, not an empty set.

## Question 2 — authors in both the 'classic' and 'modern' sets → `INTERSECT`

Each side of the query is "the set of authors tagged with this one Era tag." Being in *both* sets
is set intersection by definition, so `INTERSECT` reads exactly like the business question, with
no `HAVING COUNT(DISTINCT tag) = 2`-style workaround needed to express "has both."

```sql
SELECT a.AuthorId, a.Name
FROM Authors a
JOIN AuthorTags at ON at.AuthorId = a.AuthorId
JOIN Tags t ON t.TagId = at.TagId
WHERE t.Category = 'Era' AND t.TagName = 'classic'

INTERSECT

SELECT a.AuthorId, a.Name
FROM Authors a
JOIN AuthorTags at ON at.AuthorId = a.AuthorId
JOIN Tags t ON t.TagId = at.TagId
WHERE t.Category = 'Era' AND t.TagName = 'modern'

ORDER BY 1;
```

| AuthorId | Name        |
|----------|-------------|
| 3        | Nadia Index |

Only Nadia Index is tagged both `classic` and `modern` in the `Era` category in the seed data;
everyone else has at most one of the two.

## Question 3 — combined distinct tag list across two categories → `UNION`

`'classic'` exists as both an `Era` tag and a `Style` tag — two different `TagId`s, same name. A
plain concatenation (`UNION ALL`, or gluing two result sets together in application code) would
show `'classic'` twice; `UNION`'s built-in de-duplication is exactly "combined **distinct** tag
list" from the spec, for free.

```sql
SELECT TagName
FROM Tags
WHERE Category = 'Era'

UNION

SELECT TagName
FROM Tags
WHERE Category = 'Style'

ORDER BY 1;
```

| TagName      |
|--------------|
| aphorism     |
| classic      |
| contemporary |
| epigram      |
| modern       |

Five rows, not six — `classic` collapsed from its two source rows into one, which is the whole
point of using `UNION` here instead of `UNION ALL`.

## Verification

All three queries were run for real (Python's bundled `sqlite3`, which supports `UNION`,
`INTERSECT`, and `EXCEPT` natively — same reasoning as pieces 1 and 2, no local SQL Server
instance on hand). One SQLite-specific wrinkle worth flagging: the original queries ordered by
`AuthorId`/`TagName` directly, which SQLite rejected as ambiguous (`ORDER BY` in a compound query
can collide with a same-named column from an underlying join, e.g. `Quotes.AuthorId` alongside
`Authors.AuthorId`). Both SQL Server and SQLite actually agree here: a `UNION`/`INTERSECT`/`EXCEPT`
query's `ORDER BY` can only see the final output column list, not the tables behind either side, so
referencing the column by **ordinal position** (`ORDER BY 1`) is the portable fix — used in all
three queries above instead of the column name.

## GitHub link

https://github.com/thinkbridge-thinkschool/thinkschool-Shagun_Yadav/tree/main/day-7/piece3

## Notes for mentor

Schema is `Authors`/`Quotes` (copied from `day-7/piece1`, unmodified) plus two new tables,
`Tags` and `AuthorTags`, added specifically for this exercise. All author names, tag names, and
quote text are synthetic placeholders, not real attributed data.

## What did I learn this session?

`ORDER BY` after a `UNION`/`INTERSECT`/`EXCEPT` only ever sees the compound query's *output*
columns — it can't see back into either side's `FROM`/`JOIN` tables. That's why `ORDER BY
AuthorId` failed here even though `AuthorId` was clearly the first output column: both the
`Quotes` and `AuthorTags` joins also have a column literally named `AuthorId`, and the engine
won't guess which one you mean. Referencing the column by position (`ORDER BY 1`) sidesteps the
ambiguity entirely and works identically across engines.

## What would break this?

- Question 2's `INTERSECT` is scoped to `Category = 'Era'` on both sides on purpose. Drop that
  filter and match on `TagName` alone, and an author tagged `'classic'` in the *Style* category
  (like Faye Cursor here) would get folded into "the classic set" even though that's a different
  taxonomy than the `Era` sense the business question means — it happens not to change this
  particular result (no author has *only* the Style `'classic'` tag and also `'modern'`), but it's
  a latent bug waiting for the right data to surface it.
- Question 1's two `SELECT`s must return the exact same column list and types for `EXCEPT` to be
  legal at all — if `Authors.Name` were ever widened (e.g. `NVARCHAR(100)` to `NVARCHAR(200)`) on
  only one side of a similar future query, some engines are lenient about implicit conversion and
  some aren't; it's the kind of mismatch that only fails at the database boundary, not in the
  query's logic.
- Question 3 assumes `TagName` is the only column that matters for "distinct tag list." If a
  caller needed to know *which* category each surviving name came from, this exact `UNION` throws
  that information away — `'classic'` in the result no longer says whether it was the Era row, the
  Style row, or both.
