"use server";

import { revalidatePath } from "next/cache";
import { getTranslations } from "next-intl/server";
import { env } from "@/lib/env";
import { getSessionId } from "@/lib/auth/session";
import { deriveOccupations } from "@/lib/api/occupation-derive";
import { getResumes, getParsedResumeOccupations } from "@/lib/api/resumes";
import { getParsedResumeSkills, searchSkills } from "@/lib/api/skills";
import type { OccupationCandidate } from "@/lib/dto/match-preferences";
import type { SkillOption } from "@/lib/dto/skills";
import { pickPrimaryResume } from "@/components/settings/match-preferences-shared";
import {
  makeSetMatchPreferencesSchema,
  type SetMatchPreferencesInput,
} from "./match-preferences-schemas";
import { mapActionError } from "./_action-error";

function authHeaders(sessionId: string): HeadersInit {
  return {
    Authorization: `Bearer ${sessionId}`,
    "Content-Type": "application/json",
  };
}

export type ActionResult =
  | { success: true }
  | { success: false; error: string };

/**
 * F4-12 PR-B (ADR 0076) — sparar användarens matchnings-önskemål
 * (yrkesgrupper + regioner + kommuner + anställningsformer) via
 * `PUT /api/v1/me/match-preferences` (204 No Content vid lyckat).
 * Speglar `me.ts` `updateMyProfileAction`: getSessionId-vakt → safeParse →
 * fetch → `mapActionError` på !ok (body läses ALDRIG, TD-10) → network-
 * fallback → `revalidatePath`.
 *
 * Full-replace: `input` bär HELA den aktuella mängden per dimension. Alla
 * fyra tomma är tillåtet (rensar önskemålen — ärlig not-assessed-state).
 *
 * Spår 3 PR-D (ADR 0076-amendment 2026-06-21): region + kommun skickas i SAMMA
 * PUT (atomiskt). Eftersom det är ett full-replace ersätts hela ort-paret som
 * en enhet, så ett spar av regioner aldrig nollar angivna kommuner och vice
 * versa (CTO/architect NOTE-1). `parsed.data` bär nu `preferredMunicipalities`.
 *
 * Revaliderar både `/installningar` (kortet) och `/oversikt` (setup-nudgen
 * styrs av `hasStatedDesiredOccupation` som ändras av detta skriv).
 */
export async function updateMatchPreferencesAction(
  input: SetMatchPreferencesInput
): Promise<ActionResult> {
  const ts = await getTranslations("settings");
  const te = await getTranslations("errors");
  const sessionId = await getSessionId();
  if (!sessionId)
    return { success: false, error: ts("matchPrefs.errors.notLoggedIn") };

  const t = await getTranslations("validation");
  const parsed = makeSetMatchPreferencesSchema(t).safeParse(input);
  if (!parsed.success) {
    return {
      success: false,
      error: parsed.error.issues[0]?.message ?? ts("matchPrefs.errors.invalidInput"),
    };
  }

  try {
    const res = await fetch(`${env.BACKEND_URL}/api/v1/me/match-preferences`, {
      method: "PUT",
      headers: authHeaders(sessionId),
      body: JSON.stringify(parsed.data),
      cache: "no-store",
    });

    if (!res.ok) {
      return {
        success: false,
        error: mapActionError(res, ts("matchPrefs.errors.saveFailed"), te),
      };
    }
  } catch {
    return {
      success: false,
      error: ts("matchPrefs.errors.network"),
    };
  }

  revalidatePath("/installningar");
  revalidatePath("/oversikt");
  return { success: true };
}

export type DeriveResult =
  | { success: true; candidates: ReadonlyArray<OccupationCandidate> }
  | { success: false; error: string };

/**
 * Tunn server-action-wrapper kring `deriveOccupations`-BFF:en så
 * matchnings-kortets klient-ö kan be om yrkestitel-förslag UTAN att läsa
 * backend direkt (server-only-gränsen bevaras; Bearer-sessionen exponeras
 * aldrig mot klienten). Förslagen skrivs ALDRIG — användaren bekräftar genom
 * att toggla kryssrutorna och därefter spara (propose-and-approve, ADR 0040
 * Beslut 4).
 */
export async function deriveOccupationsAction(
  title: string
): Promise<DeriveResult> {
  const ts = await getTranslations("settings");
  if (typeof title !== "string") {
    return { success: false, error: ts("matchPrefs.errors.invalidTitle") };
  }

  const result = await deriveOccupations(title);
  switch (result.kind) {
    case "ok":
      return { success: true, candidates: result.data.candidates };
    case "unauthorized":
      return { success: false, error: ts("matchPrefs.errors.notLoggedIn") };
    case "rateLimited":
      return {
        success: false,
        error: ts("matchPrefs.errors.tooManyAttempts"),
      };
    default:
      return {
        success: false,
        error: ts("matchPrefs.errors.suggestFailed"),
      };
  }
}

/**
 * F4-rework STEG A (ADR 0076) — diskriminerad union för "Föreslå utifrån mitt
 * CV". Varje variant motsvarar EN distinkt UI-state i dialogens YRKEN-sektion:
 *  - `unauthorized` → utloggad (lugn fel-rad);
 *  - `noCv`         → inget CV uppladdat (tom-state-Alert med "Importera CV");
 *  - `noRole`       → CV finns men ingen läsbar yrkesroll → lugn rad;
 *  - `candidates`   → härledda yrkesgrupp-kandidater (pre-kryssad förhands-
 *                     granskning, propose-and-approve);
 *  - `error`        → kunde inte läsas just nu (lugn fel-rad).
 */
export type CvSuggestResult =
  | { kind: "candidates"; candidates: ReadonlyArray<OccupationCandidate> }
  | { kind: "noCv" }
  | { kind: "noRole" }
  | { kind: "unauthorized" }
  | { kind: "error" };

/**
 * F4-rework STEG A (ADR 0076/0071, deterministisk — INGEN AI) — härleder
 * yrkesgrupp-förslag ur användarens CV genom att kedja TVÅ redan auditerade,
 * server-only-endpoints: `getResumes()` (paged `ResumeListItemDto` med
 * `isPrimary` + `latestRole`, PLAINTEXT-projektion, INGEN DEK) → väljer det
 * primära/senaste CV:t → om `latestRole` finns, kör samma `deriveOccupations`-
 * BFF som yrkestitel-förslaget. Ingen ny backend-yta, ingen ny CV-läs-yta,
 * ingen ny PII-egress: `latestRole` är den denormaliserade plaintext-rollen som
 * redan exponeras via CV-listan, och titel-derive är samma anrop som titel-
 * förslaget gör.
 *
 * Förslaget skrivs ALDRIG — användaren godkänner genom att lägga till chips och
 * därefter spara i dialogen (propose-and-approve, ADR 0071/0076). Bearer-
 * sessionen stannar server-side (server-only).
 */
export async function suggestOccupationsFromCvAction(): Promise<CvSuggestResult> {
  const resumesResult = await getResumes(1, 50);
  switch (resumesResult.kind) {
    case "unauthorized":
      return { kind: "unauthorized" };
    case "ok":
      break;
    default:
      return { kind: "error" };
  }

  const resume = pickPrimaryResume(resumesResult.data.items);
  if (resume === null) return { kind: "noCv" };

  const latestRole = resume.latestRole?.trim() ?? "";
  if (latestRole.length === 0) return { kind: "noRole" };

  const derived = await deriveOccupations(latestRole);
  switch (derived.kind) {
    case "ok":
      return derived.data.candidates.length === 0
        ? { kind: "noRole" }
        : { kind: "candidates", candidates: derived.data.candidates };
    case "unauthorized":
      return { kind: "unauthorized" };
    default:
      return { kind: "error" };
  }
}

/**
 * Fas 4 onboarding (ADR 0076, CTO Variant B 2026-06-21) — CV-suggest sourced from a SPECIFIC
 * freshly-uploaded `parsed_resume` (the wizard receives its `parsedResumeId` from the welcome
 * upload). Distinct from {@link suggestOccupationsFromCvAction}, which reads the promoted
 * `Resume`'s `latestRole`: a brand-new user who just uploaded has NO promoted Resume yet (only a
 * PendingReview parsed_resume), so the latestRole path returns `noCv`. This reads the non-PII
 * `occupation_proposals` already derived at import — no DEK, no CV-PII egress (the backend
 * projects the jsonb column).
 *
 * Maps to the SAME {@link CvSuggestResult} union so the YRKEN section renders unchanged: an
 * empty proposal list → `noRole` (CV read, no occupation derivable — honest, not a failure); a
 * missing/cross-user/promoted artifact → `noCv` (404 from the owner-scoped, fail-closed read).
 * The proposal is never written (propose-and-approve, ADR 0040/0071).
 */
export async function suggestOccupationsFromParsedResumeAction(
  parsedResumeId: string
): Promise<CvSuggestResult> {
  if (typeof parsedResumeId !== "string" || parsedResumeId.length === 0) {
    return { kind: "noCv" };
  }

  const result = await getParsedResumeOccupations(parsedResumeId);
  switch (result.kind) {
    case "unauthorized":
      return { kind: "unauthorized" };
    case "notFound":
      return { kind: "noCv" };
    case "ok":
      return result.data.length === 0
        ? { kind: "noRole" }
        : { kind: "candidates", candidates: result.data };
    default:
      return { kind: "error" };
  }
}

export type SkillSearchResult =
  | { success: true; options: ReadonlyArray<SkillOption> }
  | { success: false; error: string };

/**
 * STEG 3 / ADR 0079 (Beslut 1) — thin server-action wrapper around the
 * `searchSkills` BFF so the skill-chip client island can type-ahead WITHOUT
 * reading the backend directly (server-only boundary preserved; Bearer session
 * never reaches the client). The flat 20k skill vocabulary has no hierarchy, so
 * "add a skill" is search-as-you-type (Klas option A) rather than a cascade.
 *
 * Returns options on success and a graceful empty list (never throws) on every
 * failure mode — the typeahead degrades to "no results" rather than surfacing a
 * loud error mid-keystroke. Blank/short queries short-circuit to `[]` in the
 * BFF (no round-trip). The added skill is never written until the host saves
 * (propose-and-approve, ADR 0040/0071).
 */
export async function searchSkillsAction(
  query: string
): Promise<SkillSearchResult> {
  if (typeof query !== "string") {
    return { success: true, options: [] };
  }

  const result = await searchSkills(query);
  switch (result.kind) {
    case "ok":
      return { success: true, options: result.data };
    case "unauthorized": {
      const ts = await getTranslations("settings");
      return { success: false, error: ts("matchPrefs.errors.notLoggedIn") };
    }
    case "rateLimited": {
      const ts = await getTranslations("settings");
      return { success: false, error: ts("matchPrefs.errors.tooManyAttempts") };
    }
    default:
      // Graceful: a transient read failure renders as "no results", not a
      // blocking error (the user can keep typing / add nothing and move on).
      return { success: true, options: [] };
  }
}

/**
 * STEG 3 / ADR 0079 (Beslut 1) — discriminated union for the CV skill
 * suggestion, parallel to {@link CvSuggestResult} for occupations so the
 * SkillSection renders the same honest states. `candidates` carries the full
 * `{conceptId, label}` so chips can render their Swedish labels (the skill
 * taxonomy is never shipped to the FE as a tree, so there is no client-side
 * label lookup — the proposal IS the label source).
 */
export type SkillSuggestResult =
  | { kind: "candidates"; candidates: ReadonlyArray<SkillOption> }
  | { kind: "noCv" }
  | { kind: "noRole" }
  | { kind: "unauthorized" }
  | { kind: "error" };

/**
 * STEG 3 / ADR 0079 (Beslut 1) — CV skill proposals sourced from a SPECIFIC
 * freshly-uploaded `parsed_resume` (the welcome / just-uploaded path), parallel
 * to {@link suggestOccupationsFromParsedResumeAction}. Reads the owner-scoped,
 * non-PII `/skills` projection (no DEK, no CV-PII egress — the backend projects
 * the resolved concept-ids/labels). Maps to the SAME {@link SkillSuggestResult}
 * union: an empty list → `noRole` (CV read, no skill resolvable — honest, not a
 * failure); a missing/cross-user/promoted artifact → `noCv` (404 from the
 * fail-closed read). Never written (propose-and-approve, ADR 0040/0071).
 *
 * No promoted-CV (`latestRole`-style) auto-suggest path exists yet for settings
 * users — the search-add affordance covers them. A noted follow-up, not built.
 */
export async function suggestSkillsFromParsedResumeAction(
  parsedResumeId: string
): Promise<SkillSuggestResult> {
  if (typeof parsedResumeId !== "string" || parsedResumeId.length === 0) {
    return { kind: "noCv" };
  }

  const result = await getParsedResumeSkills(parsedResumeId);
  switch (result.kind) {
    case "unauthorized":
      return { kind: "unauthorized" };
    case "notFound":
      return { kind: "noCv" };
    case "ok":
      return result.data.length === 0
        ? { kind: "noRole" }
        : { kind: "candidates", candidates: result.data };
    default:
      return { kind: "error" };
  }
}
