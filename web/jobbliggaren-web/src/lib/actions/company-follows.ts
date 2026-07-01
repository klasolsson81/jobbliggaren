"use server";

import { revalidatePath } from "next/cache";
import { getTranslations } from "next-intl/server";
import {
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
 * CompanyWatchId so the toggle can unfollow by opaque id. Revalidates `/jobb` so the detail re-reads
 * follow-state on the next render.
 */
export async function followCompanyFromJobAdAction(
  jobAdId: string
): Promise<FollowCompanyResult> {
  const t = await getTranslations("jobads.actions");
  const result = await followCompanyFromJobAd(jobAdId);
  switch (result.kind) {
    case "ok":
      revalidatePath("/jobb");
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
 * #455 — stop following, by the opaque CompanyWatchId. Idempotent. Revalidates `/jobb`.
 */
export async function unfollowCompanyAction(
  companyWatchId: string
): Promise<UnfollowCompanyResult> {
  const t = await getTranslations("jobads.actions");
  const result = await unfollowCompany(companyWatchId);
  switch (result.kind) {
    case "ok":
      revalidatePath("/jobb");
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
