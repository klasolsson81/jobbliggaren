-- The junior list's floor for indexing (ADR 0166, the Amendment 2026-11-22), counts only (CLAUDE.md §9.2 (a)).
--
-- Run it on the merge day beside public-job-page-publication-count.sql, the same way:
--   ssh -o BatchMode=yes jp-vps 'sudo -n docker exec -i jobbliggaren-postgres psql -U postgres -d jobbliggaren -tA -F " | " -q -v ON_ERROR_STOP=1' < junior-list-floor-count.sql
--
-- It computes security-auditor's definition, the one PublicListFloor.SpreadOf computes: two ads are one employer when
-- they share an organisation number or a lower(btrim) company name; E is the distinct numbers plus the distinct names
-- minus the distinct pairs, over the ads with a number; S is the larger of the largest group by number and by name,
-- plus every ad without a number. JuniorItJobAdQueryReadTests runs this file over its seeds against the port's answer.
--
-- It prints label | value and nothing per employer: N and E as totals, E as "E under 25" below the threshold, S only
-- as yes or no, the outcome, and how many kommuner would meet the floor on their own.
BEGIN TRANSACTION READ ONLY;
WITH grp AS (
  SELECT concept_id FROM taxonomy_concepts
  WHERE parent_concept_id = 'apaJ_2ja_LuF' AND kind = 'OccupationGroup'
),
member AS (
  SELECT j.organization_number AS number, lower(btrim(j.company_name)) AS name, j.municipality_concept_id AS kommun
  FROM job_ads j
  WHERE j.external_id IS NOT NULL
    AND j.external_source = 'Platsbanken'
    AND j.status = 'Active'
    AND (j.expires_at IS NULL OR j.expires_at >= now())
    AND j.occupation_group_concept_id IN (SELECT concept_id FROM grp)
    AND EXISTS (
      SELECT 1 FROM job_ad_junior_assessments a
      WHERE a.job_ad_id = j.id
        AND (a.decision = 'Junior' OR (a.decision IS NULL AND a.verdict = 'Junior')))
),
whole AS (
  SELECT count(*) AS n,
         count(DISTINCT number)
           + count(DISTINCT name) FILTER (WHERE number IS NOT NULL)
           - count(DISTINCT (number, name)) FILTER (WHERE number IS NOT NULL) AS e,
         greatest(
           coalesce((SELECT max(c) FROM (SELECT count(*) AS c FROM member WHERE number IS NOT NULL GROUP BY number) t), 0),
           coalesce((SELECT max(c) FROM (SELECT count(*) AS c FROM member WHERE number IS NOT NULL GROUP BY name) t), 0))
           + count(*) FILTER (WHERE number IS NULL) AS s
  FROM member
),
by_kommun AS (
  SELECT kommun,
         count(*) AS n,
         count(DISTINCT number)
           + count(DISTINCT name) FILTER (WHERE number IS NOT NULL)
           - count(DISTINCT (number, name)) FILTER (WHERE number IS NOT NULL) AS e,
         count(*) FILTER (WHERE number IS NULL) AS without_number
  FROM member WHERE kommun IS NOT NULL GROUP BY kommun
),
kommun_largest AS (
  SELECT k.kommun,
         greatest(
           coalesce((SELECT max(c) FROM (SELECT count(*) AS c FROM member m
                     WHERE m.kommun = k.kommun AND m.number IS NOT NULL GROUP BY m.number) t), 0),
           coalesce((SELECT max(c) FROM (SELECT count(*) AS c FROM member m
                     WHERE m.kommun = k.kommun AND m.number IS NOT NULL GROUP BY m.name) t), 0)) AS largest
  FROM by_kommun k
)
SELECT 'junior members (N)', n::text FROM whole
UNION ALL SELECT 'distinct employers (E)', CASE WHEN e < 25 THEN 'E under 25' ELSE e::text END FROM whole
UNION ALL SELECT 's_ok (no employer above half)', CASE WHEN e < 25 THEN 'E under 25' ELSE (2 * s <= n)::text END FROM whole
UNION ALL SELECT 'floor holds', (n >= 100 AND e >= 25 AND 2 * s <= n)::text FROM whole
UNION ALL SELECT 'kommuner meeting the floor', count(*)::text
  FROM by_kommun k JOIN kommun_largest l USING (kommun)
  WHERE k.n >= 100 AND k.e >= 25 AND 2 * (l.largest + k.without_number) <= k.n;
ROLLBACK;
