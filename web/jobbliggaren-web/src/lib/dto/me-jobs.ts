import { z } from "zod";

/**
 * #293 / #306 (ADR 0042 Beslut E-amendment 2026-06-28) — Zod-mirror av
 * /jobb:s per-användar oläst-watermark (`JobsWatermarkDto` från
 * `Jobbliggaren.Application.JobAds.Queries.GetJobsWatermark`). ADR 0020
 * single-source: wire-formen valideras vid ACL-gränsen; datum hålls som
 * `z.string()` (presentationsansvar bor i konsumenten, jfr `me-matches.ts`).
 *
 * Arkitektoniskt identiskt med /matchningar:s `LastSeenMatchesAt`-watermark
 * (ADR 0080 Beslut 6) — en skalär tidsstämpel, INTE per-annons-state.
 * `lastSeenJobsAt == null` = kall start (första besöket / anon) ⇒ ingen
 * annons renderas som NY (W4); första besöket etablerar baseline.
 */
export const jobsWatermarkSchema = z.object({
  lastSeenJobsAt: z.string().nullable(),
});
export type JobsWatermark = z.infer<typeof jobsWatermarkSchema>;
