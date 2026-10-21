-- S0008: the search machinery. This file has no PostgreSQL counterpart to compare against, because
-- PostgreSQL did all of it with two extensions and a generated column.
--
-- Two things are being replaced:
--
--   1. Full-text search. PostgreSQL kept a generated `tsvector` column on `listings`, weighted so
--      the name outranked the description, and queried it with websearch_to_tsquery + ts_rank_cd.
--      SQL Server's full-text search indexes the real columns instead, so the stored column goes
--      away entirely and the weighting moves into the query: PublicSearchEndpoints asks
--      CONTAINSTABLE for the name and the description separately and weights the name's rank higher.
--
--   2. Trigram similarity (pg_trgm). There is no equivalent at all -- SOUNDEX and DIFFERENCE match
--      by sound, not by spelling, and would lose the typo tolerance this site depends on: "patan"
--      finding Patancheru is a verified behaviour, not a nicety. So the trigrams are stored in a
--      table and the similarity is computed from them, which is what pg_trgm does internally.
--
-- The similarity is pg_trgm's own: shared trigrams / (mine + theirs - shared), the Jaccard index.
-- Words are padded the way pg_trgm pads them (two spaces before, one after), so "patan" and
-- "patancheru" share enough to score well. It is close to pg_trgm, not identical: PostgreSQL also
-- folds accents through `unaccent`, which is not reproduced here.

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- ------------------------------------------------------------ full-text
-- One catalog for the database. Azure SQL Database supports this as written; see docs/deployment.md.
CREATE FULLTEXT CATALOG hl_search AS DEFAULT;
GO

-- CHANGE_TRACKING = AUTO keeps the index current as rows change, with no job to run.
-- LANGUAGE 1033 is British/US English word breaking; the content is English (decision D16).
CREATE FULLTEXT INDEX ON listings (
    name LANGUAGE 1033,
    description LANGUAGE 1033
)
KEY INDEX listings_pkey
ON hl_search
WITH (CHANGE_TRACKING = AUTO);
GO

-- ------------------------------------------------------------- trigrams
/* A tally table. Splitting a string into trigrams means "one row per starting position", and
   SQL Server has no generate_series; a small table of numbers is the usual stand-in. 500 rows is
   well past the longest name any of these tables allows. */
CREATE TABLE hl_numbers (n int NOT NULL CONSTRAINT hl_numbers_pkey PRIMARY KEY);
GO

INSERT INTO hl_numbers (n)
SELECT TOP (500) ROW_NUMBER() OVER (ORDER BY (SELECT NULL))
FROM sys.all_objects;
GO

/* The trigrams of one piece of text, as a table.
   Inline (a single RETURN of a SELECT) so the optimiser folds it into the calling query instead of
   running it row by row.

   Punctuation becomes a space so that "k.p.h.b" and "kphb" break the same way. TRANSLATE needs both
   arguments the same length, hence the matching run of spaces. */
CREATE FUNCTION hl_trigrams(@text nvarchar(400))
RETURNS TABLE
AS
RETURN
    WITH words AS (
        SELECT value AS word
        FROM STRING_SPLIT(
            /* REPLICATE rather than a literal run of spaces: TRANSLATE insists both arguments are
               the same length, and counting 23 spaces by eye is exactly the kind of thing that is
               wrong once and then wrong for ever. */
            TRANSLATE(
                LOWER(@text),
                '.,;:!?()[]{}''"/\-_&+@#*',
                REPLICATE(' ', LEN('.,;:!?()[]{}''"/\-_&+@#*'))),
            ' ')
        WHERE value <> ''
    ),
    padded AS (
        -- pg_trgm's padding: two spaces in front, one behind, so the start of a word scores.
        SELECT '  ' + word + ' ' AS text_padded FROM words
    )
    SELECT DISTINCT SUBSTRING(p.text_padded, n.n, 3) AS trigram
    FROM padded p
    -- DATALENGTH, not LEN: LEN ignores trailing spaces and would drop the last trigram of every word.
    JOIN hl_numbers n ON n.n <= (DATALENGTH(p.text_padded) / 2) - 2;
GO

/* The stored trigrams of everything searched by name. One table for three kinds of row, because the
   query shape is identical for each and a single index serves them all. */
CREATE TABLE search_trigrams (
    entity_type  varchar(20) NOT NULL,
    entity_id    bigint NOT NULL,
    trigram      nvarchar(3) NOT NULL,
    /* How many trigrams that row's name has in total. Denormalised on purpose: the similarity
       formula needs it for every candidate row, and reading it from the row being matched saves a
       second aggregate over this table. */
    total_count  int NOT NULL,
    CONSTRAINT search_trigrams_pkey PRIMARY KEY (entity_type, entity_id, trigram),
    CONSTRAINT search_trigrams_entity_type_check CHECK (entity_type IN ('locality', 'category', 'listing'))
);
GO

-- The lookup the search actually does: given a trigram, which rows have it.
CREATE INDEX search_trigrams_lookup_idx ON search_trigrams (trigram, entity_type)
    INCLUDE (entity_id, total_count);
GO

/* Rebuilds the trigrams for one row. Called by the triggers below, and by the reference seeds after
   a bulk load. Deliberately a procedure rather than inline SQL in three triggers: one definition of
   what a trigram row looks like. */
CREATE PROCEDURE hl_refresh_trigrams
    @entity_type varchar(20),
    @entity_id   bigint,
    @text        nvarchar(400)
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM search_trigrams WHERE entity_type = @entity_type AND entity_id = @entity_id;

    INSERT INTO search_trigrams (entity_type, entity_id, trigram, total_count)
    SELECT @entity_type, @entity_id, t.trigram, COUNT(*) OVER ()
    FROM hl_trigrams(@text) t;
END;
GO

/* Keeping them current.
   A trigger per table, firing on insert and on a name change, because a name that changed and a
   trigram table that did not is a search that quietly stops finding things. Deletes are handled by
   the same statement: the rows are removed and nothing re-inserts them.

   Set-based, not row-by-row: these fire on seed loads of a hundred rows at a time. */
CREATE TRIGGER localities_trigrams ON localities AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    DELETE st
    FROM search_trigrams st
    WHERE st.entity_type = 'locality'
      AND (st.entity_id IN (SELECT id FROM deleted) OR st.entity_id IN (SELECT id FROM inserted));

    INSERT INTO search_trigrams (entity_type, entity_id, trigram, total_count)
    SELECT 'locality', i.id, t.trigram, COUNT(*) OVER (PARTITION BY i.id)
    FROM inserted i
    CROSS APPLY hl_trigrams(i.name) t;
END;
GO

CREATE TRIGGER categories_trigrams ON categories AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    DELETE st
    FROM search_trigrams st
    WHERE st.entity_type = 'category'
      AND (st.entity_id IN (SELECT id FROM deleted) OR st.entity_id IN (SELECT id FROM inserted));

    INSERT INTO search_trigrams (entity_type, entity_id, trigram, total_count)
    SELECT 'category', i.id, t.trigram, COUNT(*) OVER (PARTITION BY i.id)
    FROM inserted i
    CROSS APPLY hl_trigrams(i.name) t;
END;
GO

CREATE TRIGGER listings_trigrams ON listings AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    DELETE st
    FROM search_trigrams st
    WHERE st.entity_type = 'listing'
      AND (st.entity_id IN (SELECT id FROM deleted) OR st.entity_id IN (SELECT id FROM inserted));

    INSERT INTO search_trigrams (entity_type, entity_id, trigram, total_count)
    SELECT 'listing', i.id, t.trigram, COUNT(*) OVER (PARTITION BY i.id)
    FROM inserted i
    CROSS APPLY hl_trigrams(i.name) t;
END;
GO

/* The similarity itself, as a function so the three call sites read the same.
   Returns 0 to 1 and never NULL: a name with no trigrams in common scores 0 rather than nothing.

   It is an inline table-valued function, not a scalar one, because a scalar function in a WHERE
   clause makes SQL Server run the whole query row by row. Call it with CROSS APPLY. */
CREATE FUNCTION hl_similarity(@entity_type varchar(20), @entity_id bigint, @text nvarchar(400))
RETURNS TABLE
AS
RETURN
    WITH mine AS (SELECT trigram FROM hl_trigrams(@text)),
    theirs AS (
        SELECT trigram, total_count
        FROM search_trigrams
        WHERE entity_type = @entity_type AND entity_id = @entity_id
    ),
    counts AS (
        SELECT
            (SELECT COUNT(*) FROM mine) AS mine_count,
            ISNULL((SELECT MAX(total_count) FROM theirs), 0) AS theirs_count,
            (SELECT COUNT(*) FROM mine m JOIN theirs t ON t.trigram = m.trigram) AS shared_count
    )
    SELECT CASE
               WHEN mine_count + theirs_count - shared_count = 0 THEN 0.0
               ELSE CAST(shared_count AS float) / (mine_count + theirs_count - shared_count)
           END AS similarity
    FROM counts;
GO
