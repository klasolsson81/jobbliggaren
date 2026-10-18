-- Listed-ad supply by taxonomy concept id, for a city- or occupation-hub gate (docs/runbooks/seo-measurement.md §3).
-- Prints concept ids, counts and one boolean only: no ad id, no title, no company name, no text, and no figure about
-- any single employer (CLAUDE.md §9.2 (a)). Run read-only.
--
-- A listed ad is the publication predicate's (JobAdPublication.IsListed, ADR 0160 D2): imported from the published
-- source (external_source, never the user-settable source), Active, and deadline empty or not passed. The filter is
-- the same as in public-job-page-publication-count.sql.
--
-- Row kinds (first column):
--   T  totals:     listed, no_kommun, remote, new_7d, distinct_employers, kommuner_below_e, lan_below_e, groups_below_e
--   K  kommun:     concept id | listed | distinct employers | s_ok
--   L  län:        concept id | listed | distinct employers
--   G  yrkesgrupp: concept id | listed | distinct employers | s_ok | distinct kommuner
-- K, L and G rows are printed only for a place or group with at least 25 distinct employers (the hub gate's E), so that
-- no row's counts can be one employer's own; the T rows count the places and groups below that.
-- s_ok is the gate's S test as a boolean: true when no single employer holds more than half of the listed ads.
-- The per-employer counts behind it stay inside the statement and are never selected out.
SET statement_timeout = '120s';
SET default_transaction_read_only = on;

WITH listed AS (
    SELECT municipality_concept_id AS kommun, region_concept_id AS lan,
           occupation_group_concept_id AS grp, lower(btrim(company_name)) AS emp,
           remote, published_at
    FROM job_ads
    WHERE status = 'Active'
      AND external_source = 'Platsbanken'
      AND (expires_at IS NULL OR expires_at >= now())
),
k AS (
    SELECT s.kommun, s.n, s.emps, (t.top * 2 <= s.n) AS s_ok
    FROM (SELECT kommun, count(*) AS n, count(DISTINCT emp) AS emps
          FROM listed WHERE kommun IS NOT NULL GROUP BY kommun) s
    JOIN (SELECT kommun, max(c) AS top FROM (SELECT kommun, emp, count(*) AS c FROM listed
          WHERE kommun IS NOT NULL GROUP BY kommun, emp) x GROUP BY kommun) t USING (kommun)
),
l AS (
    SELECT lan, count(*) AS n, count(DISTINCT emp) AS emps FROM listed WHERE lan IS NOT NULL GROUP BY lan
),
g AS (
    SELECT s.grp, s.n, s.emps, s.kommuner, (t.top * 2 <= s.n) AS s_ok
    FROM (SELECT grp, count(*) AS n, count(DISTINCT emp) AS emps, count(DISTINCT kommun) AS kommuner
          FROM listed WHERE grp IS NOT NULL GROUP BY grp) s
    JOIN (SELECT grp, max(c) AS top FROM (SELECT grp, emp, count(*) AS c FROM listed
          WHERE grp IS NOT NULL GROUP BY grp, emp) x GROUP BY grp) t USING (grp)
)
SELECT 'T' AS k, 'listed' AS id, count(*)::text AS n, '' AS a, '' AS b, '' AS c FROM listed
UNION ALL SELECT 'T', 'no_kommun', count(*)::text, '', '', '' FROM listed WHERE kommun IS NULL
UNION ALL SELECT 'T', 'remote', count(*)::text, '', '', '' FROM listed WHERE remote
UNION ALL SELECT 'T', 'new_7d', count(*)::text, '', '', '' FROM listed WHERE published_at >= now() - interval '7 days'
UNION ALL SELECT 'T', 'distinct_employers', count(DISTINCT emp)::text, '', '', '' FROM listed
UNION ALL SELECT 'T', 'kommuner_below_e', count(*)::text, '', '', '' FROM k WHERE emps < 25
UNION ALL SELECT 'T', 'lan_below_e', count(*)::text, '', '', '' FROM l WHERE emps < 25
UNION ALL SELECT 'T', 'groups_below_e', count(*)::text, '', '', '' FROM g WHERE emps < 25
UNION ALL SELECT 'K', kommun, n::text, emps::text, s_ok::text, '' FROM k WHERE emps >= 25
UNION ALL SELECT 'L', lan, n::text, emps::text, '', '' FROM l WHERE emps >= 25
UNION ALL SELECT 'G', grp, n::text, emps::text, s_ok::text, kommuner::text FROM g WHERE emps >= 25;
