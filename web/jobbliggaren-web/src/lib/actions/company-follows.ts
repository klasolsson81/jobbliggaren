"use server";

import { revalidatePath } from "next/cache";
import { getTranslations } from "next-intl/server";
import { z } from "zod";
import {
  followCompany,
  followCompanyFromJobAd,
  setWatchFilter,
  unfollowCompany,
} from "@/lib/api/company-follows";

export type FollowCompanyResult =
  | { success: true; companyWatchId: string }
  | { success: false; error: string };

export type UnfollowCompanyResult =
  | { success: true }
  | { success: false; error: string };

/**
 * #455 — follow a job ad's employer. Idempotent server-side (resurrect + race-safe). Returns the
 * CompanyWatchId so the toggle can unfollow by opaque id.
 *
 * This toggle lives INSIDE the job-ad detail modal, which is an intercepted route at `/jobb/[id]`.
 * Revalidating `/jobb` would re-render that route and re-suspend the open modal to its dark scrim
 * fallback mid-action — the #141 trap (see `setWatchFilterAction`). The toggle updates its own
 * follow-state optimistically from the returned CompanyWatchId, so `/jobb` needs no server
 * revalidate. Only `/foretag` (#448 — the followed-companies list) is revalidated.
 */
export async function followCompanyFromJobAdAction(
  jobAdId: string
): Promise<FollowCompanyResult> {
  const t = await getTranslations("jobads.actions");
  const result = await followCompanyFromJobAd(jobAdId);
  switch (result.kind) {
    case "ok":
      revalidatePath("/foretag");
      return { success: true, companyWatchId: result.data.companyWatchId };
    case "unauthorized":
      return { success: false, error: t("notLoggedIn") };
    case "notFound":
      return { success: false, error: t("followCompanyNotFound") };
    case "forbidden":
    case "rateLimited":
    case "error":
      return { success: false, error: t("followCompanyFailed") };
  }
}

/**
 * #454 / #560 PR-C — follow an employer directly by org.nr (the /foretag lookup card's "bevaka" AND the
 * /foretag/sok per-row "Bevaka"; works for a 0-ad company the by-job-ad path cannot reach). Idempotent
 * server-side (resurrect + race-safe). Revalidates `/foretag` (the watch list gains the row), `/jobb`
 * (detail toggles re-read state), and `/foretag/sok` (the search results' follow-overlay re-reads on the
 * next render — the optimistic button bridges the interim).
 */
export async function followCompanyAction(
  orgNr: string
): Promise<FollowCompanyResult> {
  const t = await getTranslations("jobads.actions");
  const result = await followCompany(orgNr);
  switch (result.kind) {
    case "ok":
      revalidatePath("/jobb");
      revalidatePath("/foretag");
      revalidatePath("/foretag/sok");
      return { success: true, companyWatchId: result.data.companyWatchId };
    case "unauthorized":
      return { success: false, error: t("notLoggedIn") };
    case "notFound":
    case "forbidden":
    case "rateLimited":
    case "error":
      return { success: false, error: t("followCompanyFailed") };
  }
}

/**
 * #455 / #448 / #560 PR-C — stop following, by the opaque CompanyWatchId. Idempotent. Revalidates
 * `/foretag` (the followed-companies list drops the row on the next RSC render — server state drives
 * the removal, no client-side optimistic copy; CTO Q4 2026-07-01, §5) and `/foretag/sok` (the search
 * results' follow-overlay re-reads). NOT `/jobb`: the job-ad detail toggle that also calls this lives
 * inside the intercepted `/jobb/[id]` modal and flips its own state optimistically, so revalidating
 * `/jobb` would only re-suspend the open modal to its dark scrim mid-action (the #141 trap).
 */
export async function unfollowCompanyAction(
  companyWatchId: string
): Promise<UnfollowCompanyResult> {
  const t = await getTranslations("jobads.actions");
  const result = await unfollowCompany(companyWatchId);
  switch (result.kind) {
    case "ok":
      revalidatePath("/foretag");
      revalidatePath("/foretag/sok");
      return { success: true };
    case "unauthorized":
      return { success: false, error: t("notLoggedIn") };
    case "notFound":
    case "forbidden":
    case "rateLimited":
    case "error":
      return { success: false, error: t("unfollowCompanyFailed") };
  }
}

/**
 * Bevakning F4b (#803) — replace ONE watch's notification filter.
 *
 * The two geo axes are kept SEPARATE all the way to the wire. A whole-län pick stays a län concept-id;
 * expanding it into that län's municipalities would silently drop every ad tagged at län granularity
 * (no municipality) from the user's notifications. The zod guard is structural defense-in-depth — the
 * domain is the authority — but it does pin the axes apart, so a crossed mapping fails here rather than
 * being stored as a filter that matches nothing.
 *
 * An ALL-EMPTY selection is VALID: it is how the user clears the filter (the backend maps it to the
 * canonical NULL). The UI must never turn "I unchecked everything" into a validation error.
 */
const setWatchFilterInputSchema = z.object({
  municipalities: z.array(z.string()).readonly(),
  regions: z.array(z.string()).readonly(),
  onlyMatched: z.boolean(),
});

export type SetWatchFilterInput = z.infer<typeof setWatchFilterInputSchema>;

export async function setWatchFilterAction(
  companyWatchId: string,
  input: SetWatchFilterInput
): Promise<UnfollowCompanyResult> {
  const t = await getTranslations("jobads.actions");

  const parsed = setWatchFilterInputSchema.safeParse(input);
  if (!parsed.success) {
    return { success: false, error: t("setWatchFilterFailed") };
  }

  const result = await setWatchFilter(companyWatchId, parsed.data);
  switch (result.kind) {
    case "ok":
      // Only /foretag renders the filter (the row disclosure + the editor's pre-fill). The caller
      // closes the dialog BEFORE this revalidate lands — a Server Action that re-renders the RSC tree
      // unmounts an open dialog mid-flow (the #141 trap).
      revalidatePath("/foretag");
      return { success: true };
    case "unauthorized":
      return { success: false, error: t("notLoggedIn") };
    case "notFound":
    case "forbidden":
    case "rateLimited":
    case "error":
      return { success: false, error: t("setWatchFilterFailed") };
  }
}
