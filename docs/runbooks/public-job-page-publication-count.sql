-- S2-P3 (#2090, ADR 0160 D9.1): personnummer-shaped tokens in the title, the description and the company name of
-- LISTED job ads. Prints counts only: no id, no text, no token. Run read-only.
--
-- A listed ad is the publication predicate's (JobAdPublication.Classify): imported from the published source,
-- Active, and not past its deadline (ExpiresAt >= now).
--
-- No cast can fail on a data value: only string functions, translate(), a character-class regex and ascii()
-- arithmetic on strings that are digits by construction. The date test compares zero-padded two-digit strings,
-- so '09' < '12' needs no cast.
--
-- Two numbers per field, and a third for the label of the ad link:
--   shaped_ads    ads with a token of a personnummer SHAPE (10 or 12 digits, with the separators the Domain
--                 scanner accepts and the #2123 forms: spaces around the separator, '.', '/', '_', split groups)
--   validated_ads the same, and the token passes the Domain's own test: month 01-12, day 01-31 or 61-91
--                 (samordningsnummer), Luhn over the 10 significant digits. THIS IS THE GATE: it must read 0.
SET statement_timeout = '120s';

WITH listed AS (
    SELECT id, title, description, company_name, url
    FROM job_ads
    WHERE status = 'Active'
      AND external_source = 'Platsbanken'
      AND (expires_at IS NULL OR expires_at >= now())
),
fields AS (
    SELECT id, 'title' AS field, translate(title, '０１２３４５６７８９', '0123456789') AS t FROM listed
    UNION ALL
    SELECT id, 'description', translate(description, '０１２３４５６７８９', '0123456789') FROM listed
    UNION ALL
    SELECT id, 'company_name', translate(company_name, '０１２３４５６７８９', '0123456789') FROM listed
),
-- Each shape is its own scan, so a match of one cannot hide a match of another. Separators: ASCII '-' and '+',
-- the Unicode dashes U+2010-2015, U+2212, '.', '/', '_'; up to three spaces (or tabs, NBSP) either side.
tokens AS (
    -- the plain forms, 10 and 12 digits, with or without one separator
    SELECT f.id, f.field, m[1] AS token FROM fields f,
        LATERAL regexp_matches(f.t, '(?<![0-9])([0-9]{6}[-+]?[0-9]{4})(?![0-9])', 'g') AS m
    UNION ALL
    SELECT f.id, f.field, m[1] FROM fields f,
        LATERAL regexp_matches(f.t, '(?<![0-9])([0-9]{8}[-+]?[0-9]{4})(?![0-9])', 'g') AS m
    UNION ALL
    -- the #2123 forms: spaces around the separator, '.', '/', '_', any Unicode dash
    SELECT f.id, f.field, m[1] FROM fields f,
        LATERAL regexp_matches(f.t, '(?<![0-9])([0-9]{6,8}[ \t ]{0,3}[-+‐-―−./_]?[ \t ]{0,3}[0-9]{4})(?![0-9])', 'g') AS m
    UNION ALL
    -- split groups: 81 12 18-9876, 811218-98 76, 19 81 12 18 9876
    SELECT f.id, f.field, m[1] FROM fields f,
        LATERAL regexp_matches(f.t, '(?<![0-9])((?:[0-9]{2}[ \t ]{1,3}){2,4}[0-9]{2}[ \t ]{0,3}[-+‐-―−./_]?[ \t ]{0,3}[0-9]{4})(?![0-9])', 'g') AS m
    UNION ALL
    SELECT f.id, f.field, m[1] FROM fields f,
        LATERAL regexp_matches(f.t, '(?<![0-9])([0-9]{6}[-+‐-―−./_]?[0-9]{2}[ \t ]{1,3}[0-9]{2})(?![0-9])', 'g') AS m
),
digits AS (
    SELECT id, field, regexp_replace(token, '[^0-9]', '', 'g') AS d FROM tokens
),
significant AS (
    SELECT id, field, right(d, 10) AS s
    FROM digits
    WHERE length(d) IN (10, 12)
),
checked AS (
    SELECT id, field, s,
           substring(s from 3 for 2) AS mm,
           substring(s from 5 for 2) AS dd,
           (SELECT sum(
                CASE WHEN i % 2 = 1
                     THEN ((ascii(substr(s, i, 1)) - 48) * 2) / 10 + ((ascii(substr(s, i, 1)) - 48) * 2) % 10
                     ELSE (ascii(substr(s, i, 1)) - 48)
                END)
            FROM generate_series(1, 10) AS i) AS luhn_sum
    FROM significant
),
validated AS (
    SELECT id, field FROM checked
    WHERE mm BETWEEN '01' AND '12'
      AND (dd BETWEEN '01' AND '31' OR dd BETWEEN '61' AND '91')
      AND luhn_sum % 10 = 0
)
SELECT 'listed_ads' AS measure, 'all' AS field, count(*) AS ads FROM listed
UNION ALL
SELECT 'shaped_ads', field, count(DISTINCT id) FROM digits GROUP BY field
UNION ALL
SELECT 'shaped_ads', 'any', count(DISTINCT id) FROM digits
UNION ALL
SELECT 'validated_ads', field, count(DISTINCT id) FROM validated GROUP BY field
UNION ALL
SELECT 'validated_ads', 'any', count(DISTINCT id) FROM validated
UNION ALL
SELECT 'listed_ads_link_host_not_arbetsformedlingen', 'all', count(*)
FROM listed
WHERE url !~* '^https?://([a-z0-9-]+\.)*arbetsformedlingen\.se(:[0-9]+)?(/|$)'
ORDER BY 1, 2;
