import { z } from "zod";
import { pagedResult } from "./_helpers";
// #842 PR4 — the preserved-ad snapshot projects recruiter contacts through the
// SAME wire type as the live ad detail. ONE crossing schema, imported from
// ./job-ads (CTO condition 2026-07-17) — never a second, drift-prone copy.
import { adContactDtoSchema } from "./job-ads";

export const applicationStatusSchema = z.enum([
  "Draft",
  "Submitted",
  "Acknowledged",
  "InterviewScheduled",
  "Interviewing",
  "OfferReceived",
  "Accepted",
  "Rejected",
  "Withdrawn",
  "Ghosted",
]);
export type ApplicationStatus = z.infer<typeof applicationStatusSchema>;

export const followUpChannelSchema = z.enum([
  "Email",
  "LinkedIn",
  "Phone",
  "Other",
]);
export type FollowUpChannel = z.infer<typeof followUpChannelSchema>;

export const followUpOutcomeSchema = z.enum([
  "Pending",
  "Responded",
  "NoResponse",
  // ADR 0092 D4/D5: a completed contact logged today via "Logga uppföljning"
  // (never Pending, so it does not count as an overdue follow-up).
  "Logged",
]);
export type FollowUpOutcome = z.infer<typeof followUpOutcomeSchema>;

// ADR 0048 — jobb-metadata-sammanfattning projicerad i read-vägen.
// Källa: JobAd (kopplad ansökan) eller Application.ManualPosting (manuell).
// jobAdId null när källan är ManualPosting. publishedAt null för manuell (J1).
export const jobAdSummaryDtoSchema = z.object({
  jobAdId: z.string().nullable(),
  title: z.string(),
  company: z.string(),
  url: z.string().nullable(),
  source: z.string(),
  publishedAt: z.string().nullable(),
  expiresAt: z.string().nullable(),
  // #805-3: källannonsens livscykel-status ("Active" | "Archived" | "Erased" —
  // Art. 17-tombstonen joinar också hit; det writerlösa "Expired" retirerades
  // i #886), projicerad ur JobAd.Status. Detta är den ENDA sanningsenliga
  // live/borta-signalen på ansöknings-läsvägen — jobAd == null betyder "ingen
  // annonsrad alls" (manuell eller enbart brev), ALDRIG "annonsen är borta" (#821).
  //
  // null ⟺ ManualPosting: ingen JobAd-rad ⇒ ingen arkivering ⇒ ingen livs-utsaga.
  // Lös z.string() (INTE jobAdStatusSchema-enum:en): filen typar medvetet även
  // `source` löst, så ett framtida statusvärde degraderar till "inte Active"
  // (default-deny) i stället för att hard-faila parse av HELA ansökningslistan.
  // .optional() = deploy-skew-resiliens (samma konvention som fälten nedan).
  status: z.string().nullable().optional(),
});
export type JobAdSummaryDto = z.infer<typeof jobAdSummaryDtoSchema>;

export const applicationDtoSchema = z.object({
  id: z.string(),
  jobSeekerId: z.string(),
  jobAdId: z.string().nullable(),
  status: applicationStatusSchema,
  createdAt: z.string(),
  updatedAt: z.string(),
  // #336: ansökningsdatum (Application.AppliedAt). null för Draft (aldrig
  // skickad). Driver den relativa "Skickad för X dagar sedan"-taggen. nullable
  // + optional: optional ger deploy-skew-resiliens (FE deploy:ad före BE → äldre
  // svar utan fältet kraschar ej parse — samma intent som jobAd nedan).
  appliedAt: z.string().nullable().optional(),
  // nullable + optional: backend skickar alltid jobAd (null|objekt), men
  // optional ger deploy-skew-resiliens (cachead/äldre svar utan fältet
  // kraschar ej parse — architect §6 deploy-säkerhets-intent).
  jobAd: jobAdSummaryDtoSchema.nullable().optional(),
  // #630 PR 7 (design §5/§11): backend-list-DTO:n har burit dessa scalars sedan
  // PR 3 — FE-schemat släpper nu igenom dem för radens "N dagar i steget" och
  // den effektiva väntetids-taggen ("N dgr utan svar", ADR 0092 D5-klockan).
  // Rena RÅVÄRDEN för display-derivering (lib/applications/urgency.ts) — regeln
  // om NÄR något kräver åtgärd förblir backend-`attentionSignal` (SSOT).
  // .optional() = deploy-skew-resiliens (samma konvention som ovan); en saknad
  // scalar ger utelämnad siffra, aldrig ett fabricerat värde (§5).
  lastStatusChangeAt: z.string().optional(),
  lastFollowUpAt: z.string().nullable().optional(),
  // #343 (ADR 0085 §3, CTO Option a): the single highest-priority reason this
  // application needs action now, computed ONCE on the backend by
  // ApplicationAttentionEvaluator (the SSOT) and serialized by NAME. The FE only
  // reads it — the attention rule is NOT re-implemented in TypeScript (SPOT).
  // Drives the pinned "Kräver åtgärd" section on /ansokningar; priority order is
  // already encoded (the backend returns the single highest-priority signal).
  // .optional(): deploy-skew resilience (an older BE response without the field
  // does not crash parse) — a missing value is read as "None" (no attention).
  attentionSignal: z
    .enum([
      "None",
      "OfferAwaitingReply",
      "OverdueFollowUp",
      "DraftDeadlineApproaching",
      "GhostSuggested",
      "NoResponseNudge",
      "SilentAfterInterview",
    ])
    .optional(),
});
export type ApplicationDto = z.infer<typeof applicationDtoSchema>;
export type ApplicationAttentionSignal = NonNullable<
  ApplicationDto["attentionSignal"]
>;

export const followUpDtoSchema = z.object({
  id: z.string(),
  channel: followUpChannelSchema,
  scheduledAt: z.string(),
  note: z.string().nullable(),
  outcome: followUpOutcomeSchema,
  outcomeAt: z.string().nullable(),
  createdAt: z.string(),
});
export type FollowUpDto = z.infer<typeof followUpDtoSchema>;

export const noteDtoSchema = z.object({
  id: z.string(),
  content: z.string().nullable(),
  createdAt: z.string(),
});
export type NoteDto = z.infer<typeof noteDtoSchema>;

// ADR 0092 D4 — one status transition on the detail timeline. `from`/`to` are
// ApplicationStatus SmartEnum names (backend `StatusChangeDto.From/To`), resolved
// to Swedish labels FE-side via next-intl exactly like `status`. `changedAt` is
// an ISO datetime (z.string(), same convention as the other date fields). The
// backend returns these chronological (oldest-first); the FE reverses for a
// newest-first timeline. Detail-only — NOT on the list ApplicationDto (CQRS
// list != detail). This is the real source of the detail modal's TIDSLINJE and
// the "N dagar i detta steg" derivation — the previous `updatedAt`-synthesised status
// event is retired (never fabricate a transition that was not recorded, §5).
export const statusChangeDtoSchema = z.object({
  from: applicationStatusSchema,
  to: applicationStatusSchema,
  changedAt: z.string(),
});
export type StatusChangeDto = z.infer<typeof statusChangeDtoSchema>;

// #315 (ADR 0086) — frozen ad-text snapshot captured at apply-time. Mirrors the
// backend AdSnapshot projected on the detail read. It is the FALLBACK that lets
// the ad text survive the source JobAd being archived/soft-deleted (the existing
// `jobAd` field goes null then). `description` is the full ad body; it is NULL
// once the application reaches a terminal status (retention minimisation — the
// body is cleared, metadata kept). `location` is the resolved "ort" name frozen
// at apply-time. publishedAt/capturedAt are ISO datetimes (z.string(), same
// convention as the other date fields above). Detail-only — NOT on the list DTO.
export const adSnapshotDtoSchema = z.object({
  title: z.string(),
  company: z.string(),
  location: z.string().nullable(),
  url: z.string().nullable(),
  source: z.string(),
  publishedAt: z.string(),
  expiresAt: z.string().nullable(),
  description: z.string().nullable(),
  // #842 PR4 — the apply-time frozen recruiter contact block: the follow-up
  // person for THIS application. Never absent; [] when the ad held none at
  // capture or once the application reached a terminal status (the body AND the
  // contacts are dropped together — retention minimisation, ADR 0086 D3).
  // Positioned before capturedAt to mirror the backend record (key order is
  // irrelevant to zod).
  contacts: z.array(adContactDtoSchema),
  capturedAt: z.string(),
});
export type AdSnapshotDto = z.infer<typeof adSnapshotDtoSchema>;

export const applicationDetailDtoSchema = applicationDtoSchema.extend({
  coverLetter: z.string().nullable(),
  followUps: z.array(followUpDtoSchema),
  notes: z.array(noteDtoSchema),
  // ADR 0092 D4 — the status-transition timeline (chronological from the backend).
  // .optional() for deploy-skew resilience (an older BE response without the field
  // does not crash parse — same convention as `jobAd`/`appliedAt`/`preservedAd`);
  // consumers read it as `?? []`.
  statusChanges: z.array(statusChangeDtoSchema).optional(),
  // null for manual/cover-letter-only and pre-#315 applications; present for
  // JobAd-linked ones (including while the live ad still exists). .nullable()
  // .optional() for deploy-skew resilience — same convention as `jobAd`/
  // `appliedAt` above (an older BE response without the field does not crash
  // parse).
  preservedAd: adSnapshotDtoSchema.nullable().optional(),
});
export type ApplicationDetailDto = z.infer<typeof applicationDetailDtoSchema>;

export const pipelineGroupDtoSchema = z.object({
  status: applicationStatusSchema,
  count: z.number().int().nonnegative(),
  applications: z.array(applicationDtoSchema),
});
export type PipelineGroupDto = z.infer<typeof pipelineGroupDtoSchema>;

export const pipelineResponseSchema = z.array(pipelineGroupDtoSchema);

export const getApplicationsResultSchema = pagedResult(applicationDtoSchema);
export type GetApplicationsResult = z.infer<typeof getApplicationsResultSchema>;
