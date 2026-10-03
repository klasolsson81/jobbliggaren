# Match detail evidence — #1864 / #1872

## Dated corpus measurement

Measured 2026-10-03T11:07:43.477873Z using an aggregate-only READ ONLY transaction.
The denominator is job_ads.status = 'Active', exactly the production search predicate.
These are dated observations, not live constants.

| Observation | Ads |
|---|---:|
| Active | 40,285 |
| Extracted terms present | 40,285 |
| Raw payload still present | 32,675 |
| Extracted MustHave skill requirements | 922 (2.3%) |
| Extracted NiceToHave skill requirements | 1,450 (3.6%) |
| Either requirement partition | 2,118 |
| Both partitions | 254 |
| Available raw valid must_have.skills | 774 |
| Available raw valid nice_to_have.skills | 1,220 |
| Any nonempty raw must_have array | 6,271 |
| Any nonempty raw nice_to_have array | 8,916 |

148 MustHave-positive and 230 NiceToHave-positive extracted rows had no remaining raw
payload; the surviving extraction must still be shown. The mapper consumes skills only,
while raw requirements also include languages, experience and education. Empty extracted
fields do not prove the employer's prose contains no requirements.

Reproduce from the repository root (configured read-only SSH alias):

    Get-Content docs/research/issues/1864-measure-requirements.sql |
        ssh -o BatchMode=yes -o ConnectTimeout=10 jp-vps 'sudo -n docker exec -i jobbliggaren-postgres psql -X -v ON_ERROR_STOP=1 --csv -U postgres -d jobbliggaren'

The SQL uses BEGIN READ ONLY, a 30-second statement timeout, aggregate output and
ON_ERROR_STOP. It returns no ad text, recruiter contacts or account records.

## Duplicate evidence and producer verification

2,289 active ads contained equal Display labels on distinct concept ids within the same
dimension (2,330 groups); zero groups had the exact label C#. 767 ads reused concepts
across dimensions. These facts do not establish an extraction defect or justify merging
different statuses or requirement authority.

The bounded aggregate sample found eight same-preferred-label pairs (boka, hantera stress,
fylla formar, datateknik, datavetenskap, sammanfatta information, 3D-modellering and medicinsk
radiologi). The real embedded v30 taxonomy and production GroupConceptIds produce one
group per pair while preserving every member; qualified Scala/Oracle concepts remain
distinct and unknown ids remain singletons. SkillSurfaceGroupingTests derives identities
from the embedded asset, invokes the actual grouping method and checks reversed-input
determinism and partition completeness.

JobAdMatchDetailEndpointTests additionally invokes real extraction through JobAd.Import,
confirms skills via the production PATCH endpoint without any Resume, and verifies the
GET detail wire. All-covered and split-side cases preserve every legacy Display occurrence
and typed concept identity in single and batch scoring. Display arrays and verdict/grade
semantics are unchanged. The new wire field is additive and strictly validated when present;
old APIs retain display-only rendering without label deduplication.

## Scope boundaries

No matching-grade, extraction, persistence, contact-retention, OAuth or deployment change.
The broad evidence-frame-family follow-up #1644 remains separate. ADR 0076's dated amendment
and DESIGN §8 own the current detail presentation and confirmed-skill wording rules.
