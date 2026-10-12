import { z } from "zod";
import { readableInstantSchema } from "@/lib/dto/_helpers";
import { notifiableMatchGradeSchema } from "@/lib/dto/me-matches";

/**
 * #1666 — Zod mirror of `GET /api/v1/me/notification-feed` (`NotificationFeedDto`). Validated at the
 * BFF boundary and parsed again by the bell, so a field the backend adds later never reaches the
 * browser until it is named here (allow-list). Instants stay strings (ADR 0020): a token in
 * `acknowledgedThrough` carries microseconds that a JS `Date` would round away, and "Markera alla
 * som lästa" sends it back unchanged.
 */

/** Why a notification exists. One row per (source, ad); an ad can ring for both reasons. */
export const notificationFeedSourceKindSchema = z.enum(["Match", "FollowedCompanyAd"]);
export type NotificationFeedSourceKind = z.infer<typeof notificationFeedSourceKindSchema>;

export const notificationFeedItemSchema = z.object({
  kind: notificationFeedSourceKindSchema,
  jobAdId: z.uuid(),
  title: z.string(),
  companyName: z.string(),
  occurredAt: readableInstantSchema,
  isUnread: z.boolean(),
  /** The stored match grade on a `Match` row; `null` on a followed company's row. */
  grade: notifiableMatchGradeSchema.nullable(),
});
export type NotificationFeedItem = z.infer<typeof notificationFeedItemSchema>;

export const notificationFeedSourceSchema = z.object({
  kind: notificationFeedSourceKindSchema,
  /** Exact, or `null` when the source refused to count past its bound (ADR 0120): no number then. */
  unreadCount: z.number().int().nonnegative().nullable(),
  /** The token: the newest instant among rows new since the watermark, read in the bell or not, sent back to the source's read-through endpoint unchanged; `null` when nothing is new. */
  acknowledgedThrough: readableInstantSchema.nullable(),
});
export type NotificationFeedSource = z.infer<typeof notificationFeedSourceSchema>;

export const notificationFeedSchema = z.object({
  items: z.array(notificationFeedItemSchema),
  sources: z.array(notificationFeedSourceSchema),
});
export type NotificationFeed = z.infer<typeof notificationFeedSchema>;
