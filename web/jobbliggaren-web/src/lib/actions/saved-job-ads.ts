"use server";

import { revalidatePath } from "next/cache";
import { getTranslations } from "next-intl/server";
import { saveJobAd, unsaveJobAd } from "@/lib/api/saved-job-ads";

export type SaveJobAdResult =
  | { success: true }
  | { success: false; error: string };

/**
 * F6 P5 Punkt 2 Del A — server-action för att bokmärka en annons.
 * Idempotent (backend hanterar redan-sparad). Revalidate:ar `/sparade`
 * + `/jobb`-listan så list-vyer reflekterar nya bokmärket.
 */
export async function saveJobAdAction(jobAdId: string): Promise<SaveJobAdResult> {
  const t = await getTranslations("jobads.actions");
  const result = await saveJobAd(jobAdId);
  switch (result.kind) {
    case "ok":
      revalidatePath("/sparade");
      revalidatePath("/jobb");
      return { success: true };
    case "unauthorized":
      return { success: false, error: t("notLoggedIn") };
    case "notFound":
      return {
        success: false,
        error: t("savedJobAdNotFound"),
      };
    case "forbidden":
    case "rateLimited":
    case "error":
      return { success: false, error: t("savedJobAdSaveFailed") };
  }
}

/**
 * F6 P5 Punkt 2 Del A — server-action för att ta bort ett bokmärke.
 * Idempotent. Revalidate:ar `/sparade` + `/jobb`-listan.
 */
export async function unsaveJobAdAction(jobAdId: string): Promise<SaveJobAdResult> {
  const t = await getTranslations("jobads.actions");
  const result = await unsaveJobAd(jobAdId);
  switch (result.kind) {
    case "ok":
      revalidatePath("/sparade");
      revalidatePath("/jobb");
      return { success: true };
    case "unauthorized":
      return { success: false, error: t("notLoggedIn") };
    case "notFound":
    case "forbidden":
    case "rateLimited":
    case "error":
      return { success: false, error: t("savedJobAdRemoveFailed") };
  }
}
