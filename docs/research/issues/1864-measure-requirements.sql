BEGIN READ ONLY;
SET LOCAL statement_timeout = '30s';

WITH active AS (
    SELECT raw_payload, extracted_terms
    FROM public.job_ads
    WHERE status = 'Active'
), flags AS (
    SELECT
        raw_payload IS NOT NULL AS has_payload,
        extracted_terms IS NOT NULL AS has_extraction,
        EXISTS (
            SELECT 1 FROM jsonb_array_elements(COALESCE(extracted_terms, '[]'::jsonb)) t
            WHERE t->>'Kind' = 'Requirement' AND t->>'Source' = 'MustHave'
        ) AS must_terms,
        EXISTS (
            SELECT 1 FROM jsonb_array_elements(COALESCE(extracted_terms, '[]'::jsonb)) t
            WHERE t->>'Kind' = 'Requirement' AND t->>'Source' = 'NiceToHave'
        ) AS nice_terms,
        EXISTS (
            SELECT 1 FROM jsonb_array_elements(
                CASE WHEN jsonb_typeof(raw_payload#>'{must_have,skills}') = 'array'
                    THEN raw_payload#>'{must_have,skills}' ELSE '[]'::jsonb END
            ) s
            WHERE NULLIF(btrim(s->>'concept_id'), '') IS NOT NULL
                AND NULLIF(btrim(s->>'label'), '') IS NOT NULL
        ) AS valid_must_skills,
        EXISTS (
            SELECT 1 FROM jsonb_array_elements(
                CASE WHEN jsonb_typeof(raw_payload#>'{nice_to_have,skills}') = 'array'
                    THEN raw_payload#>'{nice_to_have,skills}' ELSE '[]'::jsonb END
            ) s
            WHERE NULLIF(btrim(s->>'concept_id'), '') IS NOT NULL
                AND NULLIF(btrim(s->>'label'), '') IS NOT NULL
        ) AS valid_nice_skills,
        EXISTS (
            SELECT 1 FROM jsonb_each(
                CASE WHEN jsonb_typeof(raw_payload->'must_have') = 'object'
                    THEN raw_payload->'must_have' ELSE '{}'::jsonb END
            ) e
            WHERE jsonb_typeof(e.value) = 'array' AND e.value <> '[]'::jsonb
        ) AS any_raw_must,
        EXISTS (
            SELECT 1 FROM jsonb_each(
                CASE WHEN jsonb_typeof(raw_payload->'nice_to_have') = 'object'
                    THEN raw_payload->'nice_to_have' ELSE '{}'::jsonb END
            ) e
            WHERE jsonb_typeof(e.value) = 'array' AND e.value <> '[]'::jsonb
        ) AS any_raw_nice
    FROM active
)
SELECT
    now() AS measured_at_utc,
    count(*) AS active_ads,
    count(*) FILTER (WHERE has_payload) AS with_raw_payload,
    count(*) FILTER (WHERE has_extraction) AS with_extracted_terms,
    count(*) FILTER (WHERE must_terms) AS extracted_must_have,
    count(*) FILTER (WHERE nice_terms) AS extracted_nice_to_have,
    count(*) FILTER (WHERE must_terms OR nice_terms) AS extracted_either,
    count(*) FILTER (WHERE must_terms AND nice_terms) AS extracted_both,
    count(*) FILTER (WHERE valid_must_skills) AS raw_valid_must_skills,
    count(*) FILTER (WHERE valid_nice_skills) AS raw_valid_nice_skills,
    count(*) FILTER (WHERE any_raw_must) AS any_raw_must_have_array,
    count(*) FILTER (WHERE any_raw_nice) AS any_raw_nice_to_have_array
FROM flags;

WITH terms AS MATERIALIZED (
    SELECT a.id, t->>'ConceptId' AS concept_id, t->>'Display' AS display,
        CASE WHEN t->>'Kind' = 'Skill' THEN 'Skill' ELSE t->>'Source' END AS dimension
    FROM public.job_ads a
    CROSS JOIN LATERAL jsonb_array_elements(COALESCE(a.extracted_terms, '[]'::jsonb)) t
    WHERE a.status = 'Active' AND t->>'Kind' IN ('Skill', 'Requirement')
), collisions AS (
    SELECT id, dimension, display, count(DISTINCT concept_id) AS concepts
    FROM terms
    GROUP BY id, dimension, display
    HAVING count(DISTINCT concept_id) > 1
), shared AS (
    SELECT id, concept_id
    FROM terms
    GROUP BY id, concept_id
    HAVING count(DISTINCT dimension) > 1
)
SELECT
    now() AS measured_at_utc,
    (SELECT count(DISTINCT id) FROM collisions) AS ads_same_dimension_label_distinct_concepts,
    (SELECT count(*) FROM collisions) AS colliding_groups,
    (SELECT count(DISTINCT id) FROM collisions WHERE display = 'C#') AS ads_csharp_label_distinct_concepts,
    (SELECT count(DISTINCT id) FROM shared) AS ads_same_concept_across_dimensions;

SELECT
    count(*) FILTER (
        WHERE raw_payload IS NULL AND EXISTS (
            SELECT 1 FROM jsonb_array_elements(extracted_terms) t
            WHERE t->>'Kind' = 'Requirement' AND t->>'Source' = 'MustHave'
        )
    ) AS must_terms_without_raw_payload,
    count(*) FILTER (
        WHERE raw_payload IS NULL AND EXISTS (
            SELECT 1 FROM jsonb_array_elements(extracted_terms) t
            WHERE t->>'Kind' = 'Requirement' AND t->>'Source' = 'NiceToHave'
        )
    ) AS nice_terms_without_raw_payload
FROM public.job_ads
WHERE status = 'Active';

COMMIT;
