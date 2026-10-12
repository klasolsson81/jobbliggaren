"use server";

import { getTranslations } from "next-intl/server";
import { z } from "zod";
import type { ActionResult } from "@/lib/actions/_action-result";
import { dismissNotification, markNotificationRead, markNotificationsReadThrough } from "@/lib/api/notification-feed";
import { notificationFeedSourceKindSchema } from "@/lib/dto/notification-feed";

/** A token as the API writes a `DateTimeOffset`; anything else never reaches the backend. */
const tokenSchema = z.iso.datetime({ offset: true });

/** A Server Action is a public endpoint: exactly a source and an ad id, nothing else (the read mark and the X share it). */
const rowActionSchema = z.strictObject({
  kind: notificationFeedSourceKindSchema,
  jobAdId: z.uuid(),
});

/**
 * #2130 — a click on one notification in the bell: marks that row read, in the bell and nowhere else. The
 * write sets the bell's own flag; it moves no watermark, so Översikt, `/matchningar` and
 * `/foretag/bevakade/nya` keep their numbers (Klas 2026-10-11). The API answers 204 whether or not the row
 * exists, so the outcome says nothing about anyone's rows.
 *
 * Fired WITHOUT waiting from a click that also opens the ad, often as the intercepted job modal. It does no
 * re-rendering work (`revalidatePath`, `refresh`, `updateTag`, a cookie write, `redirect`) and returns only its
 * own value. Measured 2026-10-12 (`tests/job-modal/notifications-bell.spec.ts`): the modal opens at once and
 * stays mounted while the answer is held 0 and 5 000 ms, and the write lands. Next dispatches actions one at a time.
 */
export async function markNotificationReadAction(input: {
  kind: string;
  jobAdId: string;
}): Promise<ActionResult> {
  const t = await getTranslations("common.notifications");
  const parsed = rowActionSchema.safeParse(input);
  if (!parsed.success) return { success: false, error: t("markReadFailed") };

  const result = await markNotificationRead(parsed.data.kind, parsed.data.jobAdId);
  return result.kind === "ok" ? { success: true } : { success: false, error: t("markReadFailed") };
}

/**
 * #2151 — the X on one notification in the bell: takes that notification out of the bell, and out of nothing else.
 * It sets the bell's own flags; it moves no watermark and deletes nothing, so Översikt, `/matchningar` and
 * `/foretag/bevakade/nya` keep their rows and numbers (Klas 2026-10-11, 2026-10-12). The API answers 204 whether or
 * not the row exists, so the outcome says nothing about anyone's rows.
 *
 * Fired WITHOUT waiting, like `markNotificationReadAction`: it does no re-rendering work (`revalidatePath`, `refresh`,
 * `updateTag`, a cookie write, `redirect`) and returns only its own value. A click on the X triggers it and nothing
 * else: never a render, an effect, a hover or a focus.
 */
export async function dismissNotificationAction(input: {
  kind: string;
  jobAdId: string;
}): Promise<ActionResult> {
  const t = await getTranslations("common.notifications");
  const parsed = rowActionSchema.safeParse(input);
  if (!parsed.success) return { success: false, error: t("dismissFailed") };

  const result = await dismissNotification(parsed.data.kind, parsed.data.jobAdId);
  return result.kind === "ok" ? { success: true } : { success: false, error: t("dismissFailed") };
}

/**
 * #1666, #2130 — "Markera alla som lästa" in the bell: marks each source's whole counted set read, with the
 * token the feed returned for it, through that source's own bell endpoint. An explicit act over a number
 * the user has seen. Since #2130 it writes only the bell's flags: it no longer acknowledges the sources'
 * `/seen` watermarks, so it no longer clears Översikt's count or the lists' "new" marks (Klas 2026-10-11).
 *
 * The token is sent as the original string: the API stores microseconds and a JS `Date` keeps
 * milliseconds, so a re-serialised token would land just below its own row and the badge would never
 * reach zero. A null or malformed token sends nothing for that source.
 *
 * Runs on a click only. The two calls are sequential and independent: one may succeed while the other
 * fails, and the bell fetches the feed again afterwards, so it shows what actually happened.
 */
export async function markAllNotificationsReadAction(tokens: {
  match: string | null;
  followedCompanyAd: string | null;
}): Promise<ActionResult> {
  const t = await getTranslations("common.notifications");
  const sends = [
    { kind: "Match", token: tokens.match },
    { kind: "FollowedCompanyAd", token: tokens.followedCompanyAd },
  ] as const;

  let failed = false;
  for (const { kind, token } of sends) {
    if (token === null) continue;
    if (!tokenSchema.safeParse(token).success) {
      failed = true;
      continue;
    }
    const result = await markNotificationsReadThrough(kind, token);
    if (result.kind !== "ok") failed = true;
  }

  return failed ? { success: false, error: t("markReadFailed") } : { success: true };
}
