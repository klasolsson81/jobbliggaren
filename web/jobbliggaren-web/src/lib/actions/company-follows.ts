"use server";

import { revalidatePath } from "next/cache";
import { getTranslations } from "next-intl/server";
import {
  followCompany,
  followCompanyFromJobAd,
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
 * CompanyWatchId so the toggle can unfollow by opaque id. Revalidates `/jobb` (the detail re-reads
 * follow-state) and `/foretag` (#448 — the followed-companies list re-reads on next render).
 */
export async function followCompanyFromJobAdAction(
  jobAdId: string
): Promise<FollowCompanyResult> {
  const t = await getTranslations("jobads.actions");
  const result = await followCompanyFromJobAd(jobAdId);
  switch (result.kind) {
    case "ok":
      revalidatePath("/jobb");
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
 * #454 — follow an employer directly by org.nr (the /foretag lookup card's "bevaka"; works for a
 * 0-ad company the by-job-ad path cannot reach). Idempotent server-side (resurrect + race-safe).
 * Revalidates `/foretag` (the watch list gains the row) + `/jobb` (detail toggles re-read state).
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
 * #455 / #448 — stop following, by the opaque CompanyWatchId. Idempotent. Revalidates `/jobb` (the
 * detail toggle) and `/foretag` (the followed-companies list drops the row on the next RSC render —
 * server state drives the removal, no client-side optimistic copy; CTO Q4 2026-07-01, §5).
 */
export async function unfollowCompanyAction(
  companyWatchId: string
): Promise<UnfollowCompanyResult> {
  const t = await getTranslations("jobads.actions");
  const result = await unfollowCompany(companyWatchId);
  switch (result.kind) {
    case "ok":
      revalidatePath("/jobb");
      revalidatePath("/foretag");
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
