-- S2-P3 (#2090, ADR 0160 D9.1): personnummer-shaped tokens in the title, the description, the company name, the
-- link and the application link of LISTED job ads: the five values the public page prints (the application link since
-- #2214 PR 1b). Prints counts only: no id, no text, no token. Run read-only.
--
-- The application link is counted for EVERY listed ad, not only the links the public gate admits: a 0 over the
-- superset is a 0 over the subset, and a gate in SQL would be a second copy of it (ADR 0166 D10 trigger 19). It is
-- counted twice, as stored and percent-decoded: a token behind %2D or %20 is a token to whoever reads the href.
--
-- A listed ad is the publication predicate's (JobAdPublication.Classify): imported from the published source,
-- Active, and not past its deadline (ExpiresAt >= now).
--
-- No cast can fail on a data value: only string functions, translate(), a character-class regex and ascii()
-- arithmetic on strings that are digits by construction. The date test compares zero-padded two-digit strings,
-- so '09' < '12' needs no cast.
--
-- Two numbers per field, and one more for the label of the ad link:
--   shaped_ads    ads with a token of a personnummer SHAPE (a digit run, with the separators the Domain
--                 scanner accepts and the #2123/#2137 forms: spaces around the separator, up to two separators in a row, '.', '/', '_', split groups)
--   validated_ads the same, and the token passes the Domain's own test: month 01-12, day 01-31 or 61-91
--                 (samordningsnummer), Luhn over the 10 significant digits. THIS IS THE GATE: it must read 0.
-- A reading with listed_ads near 0 is not a reading: it means the table is not the one that was meant.
SET statement_timeout = '180s';

WITH listed AS (
    SELECT id, title, description, company_name, url, coalesce(application_url, '') AS application_url
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
    UNION ALL
    SELECT id, 'url', translate(url, '０１２３４５６７８９', '0123456789') FROM listed
    UNION ALL
    SELECT id, 'application_url', translate(application_url, '０１２３４５６７８９', '0123456789') FROM listed
    UNION ALL
    -- The escapes a token's digits and separators can hide behind, upper or lower case: NBSP first (two bytes), then
    -- space, tab, '-', '+', '.', '/', '_'.
    SELECT id, 'application_url_decoded', translate(
        regexp_replace(regexp_replace(regexp_replace(regexp_replace(regexp_replace(regexp_replace(regexp_replace(
        regexp_replace(application_url, '%C2%A0', chr(160), 'gi'),
        '%20', ' ', 'g'), '%09', chr(9), 'g'), '%2D', '-', 'gi'), '%2B', '+', 'gi'), '%2E', '.', 'gi'), '%2F', '/', 'gi'),
        '%5F', '_', 'gi'),
        '０１２３４５６７８９', '0123456789') FROM listed
),
-- Each shape is its own scan, so a match of one cannot hide a match of another.
--   gap  = up to three of: space, tab, NBSP, the Unicode spaces U+2000-200A, U+202F, U+205F, U+3000, the zero-width
--          characters U+200B-200D, U+2060, U+FEFF and the soft hyphen U+00AD
--   dash = '-', '+', the Unicode dashes U+2010-2015, U+2212 MINUS, U+FF0D fullwidth hyphen-minus
--   sep  = a dash or '.', '/', '_'; the two `gap sep gap` shapes take up to TWO in a row (811218 -- 9876, #2137)
tokens AS (
    -- the plain forms, 10 and 12 digits, with or without one dash
    SELECT f.id, f.field, m[1] AS token FROM fields f,
        LATERAL regexp_matches(f.t, '(?<![0-9])([0-9]{6}[-+‐-―−－]?[0-9]{4})(?![0-9])', 'g') AS m
    UNION ALL
    SELECT f.id, f.field, m[1] FROM fields f,
        LATERAL regexp_matches(f.t, '(?<![0-9])([0-9]{8}[-+‐-―−－]?[0-9]{4})(?![0-9])', 'g') AS m
    UNION ALL
    -- the #2123 forms: gaps around the separator, '.', '/', '_', any dash
    SELECT f.id, f.field, m[1] FROM fields f,
        LATERAL regexp_matches(f.t, '(?<![0-9])([0-9]{6,8}[ \t  -   　​-‍⁠﻿­]{0,3}[-+‐-―−－./_]{0,2}[ \t  -   　​-‍⁠﻿­]{0,3}[0-9]{4})(?![0-9])', 'g') AS m
    UNION ALL
    -- split groups: 81 12 18-9876, 811218-98 76, 19 81 12 18 9876
    SELECT f.id, f.field, m[1] FROM fields f,
        LATERAL regexp_matches(f.t, '(?<![0-9])((?:[0-9]{2}[ \t  -   　​-‍⁠﻿­]{1,3}){2,4}[0-9]{2}[ \t  -   　​-‍⁠﻿­]{0,3}[-+‐-―−－./_]{0,2}[ \t  -   　​-‍⁠﻿­]{0,3}[0-9]{4})(?![0-9])', 'g') AS m
    UNION ALL
    SELECT f.id, f.field, m[1] FROM fields f,
        LATERAL regexp_matches(f.t, '(?<![0-9])([0-9]{6}[-+‐-―−－./_]?[0-9]{2}[ \t  -   　​-‍⁠﻿­]{1,3}[0-9]{2})(?![0-9])', 'g') AS m
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
