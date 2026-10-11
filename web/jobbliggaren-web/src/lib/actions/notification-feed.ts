"use server";

import { getTranslations } from "next-intl/server";
import { z } from "zod";
import type { ActionResult } from "@/lib/actions/_action-result";
import { markFollowedAdsSeen } from "@/lib/api/company-follows";
import { markMatchesSeen } from "@/lib/api/me-matches";

/** A token as the API writes a `DateTimeOffset`; anything else never reaches the backend. */
const tokenSchema = z.iso.datetime({ offset: true });

/**
 * #1666 — "Markera alla som lästa" in the bell: acknowledges each source's whole counted set through
 * its EXISTING `/seen` endpoint, with the token the feed returned for it. An explicit act over a
 * number the user has seen; the #1576 window rule binds the implicit acknowledgement a visit makes,
 * not this one (ADR 0162).
 *
 * The token is sent as the original string: the API stores microseconds and a JS `Date` keeps
 * milliseconds, so a re-serialised token would land just below its own row and the badge would never
 * reach zero. A null or malformed token sends nothing for that source — never an empty body, which the
 * backend would read as "acknowledge through now" and so swallow rows the user never saw.
 *
 * Runs on a click only. The two calls are sequential and independent: one may succeed while the other
 * fails, and the bell fetches the feed again afterwards, so it shows what actually happened.
 */
export async function markNotificationsReadAction(tokens: {
  match: string | null;
  followedCompanyAd: string | null;
}): Promise<ActionResult> {
  const t = await getTranslations("common.notifications");
  const sends = [
    { token: tokens.match, send: markMatchesSeen },
    { token: tokens.followedCompanyAd, send: markFollowedAdsSeen },
  ] as const;

  let failed = false;
  for (const { token, send } of sends) {
    if (token === null) continue;
    if (!tokenSchema.safeParse(token).success) {
      failed = true;
      continue;
    }
    const result = await send(token);
    if (result.kind !== "ok") failed = true;
  }

  return failed ? { success: false, error: t("markReadFailed") } : { success: true };
}
