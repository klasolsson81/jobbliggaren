"use server";

import { revalidatePath } from "next/cache";
import { getTranslations } from "next-intl/server";
import type { ActionResult } from "@/lib/actions/_action-result";
import {
  setJuniorQuickLink,
  setRecentSearchQuickLink,
  type QuickLinkWriteResult,
} from "@/lib/api/quick-links";
import { QUICK_LINK_MAX } from "@/lib/job-ads/quick-links";

/**
 * ADR 0188 (#2265) — shows or hides the junior quick link in the /jobb hero. The hero's × and the settings section call
 * this; the hero, /sokningar and the settings section are revalidated, since each shows the links.
 */
export async function setJuniorQuickLinkAction(enabled: boolean): Promise<ActionResult> {
  if (typeof enabled !== "boolean") return outcome({ kind: "error" }, await errors());
  return outcome(await setJuniorQuickLink(enabled), await errors());
}

/**
 * ADR 0188 (#2265) — makes one of the user's recent searches a quick link, or stops it being one. /sokningar's rows,
 * the settings section and the hero's × call this; the search itself is never touched.
 */
export async function setRecentSearchQuickLinkAction(id: string, on: boolean): Promise<ActionResult> {
  if (typeof id !== "string" || typeof on !== "boolean") return outcome({ kind: "error" }, await errors());
  return outcome(await setRecentSearchQuickLink(id, on), await errors());
}

async function errors() {
  return getTranslations("jobads.quickLinks.errors");
}

function revalidateQuickLinkPages() {
  revalidatePath("/jobb");
  revalidatePath("/sokningar");
  revalidatePath("/mina-sidor/snabblankar");
}

function outcome(
  result: QuickLinkWriteResult,
  t: Awaited<ReturnType<typeof getTranslations<"jobads.quickLinks.errors">>>,
): ActionResult {
  switch (result.kind) {
    case "ok":
      revalidateQuickLinkPages();
      return { success: true };
    case "full":
      return { success: false, error: t("full", { max: QUICK_LINK_MAX }) };
    case "conflict":
      return { success: false, error: t("conflict") };
    case "notFound":
      return { success: false, error: t("notFound") };
    case "unauthorized":
      return { success: false, error: t("notLoggedIn") };
    case "rateLimited":
      return { success: false, error: t("rateLimited", { seconds: result.retryAfterSeconds }) };
    case "error":
      return { success: false, error: t("failed") };
  }
}
