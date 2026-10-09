import type { FeedbackPageKey } from "./page-keys";

/** What one press of Send carries. The image is compared by identity: a new pick is a new image. */
export type FeedbackContent = {
  readonly page: FeedbackPageKey;
  readonly rating: number | null;
  readonly comment: string;
  readonly screenshot: Blob | null;
  readonly shareDeviceContext: boolean;
};

/** The key the last send used and the content it was used for. */
export type KeyedContent = { readonly key: string; readonly content: FeedbackContent };

function sameContent(a: FeedbackContent, b: FeedbackContent): boolean {
  return (
    a.page === b.page &&
    a.rating === b.rating &&
    a.comment.trim() === b.comment.trim() &&
    a.screenshot === b.screenshot &&
    a.shareDeviceContext === b.shareDeviceContext
  );
}

export function newSubmissionKey(): string {
  if (typeof crypto.randomUUID === "function") return crypto.randomUUID();
  // randomUUID exists only in secure contexts; a plain-http LAN dev host still has getRandomValues.
  const bytes = crypto.getRandomValues(new Uint8Array(16));
  bytes[6] = (bytes[6]! & 0x0f) | 0x40;
  bytes[8] = (bytes[8]! & 0x3f) | 0x80;
  const hex = [...bytes].map((byte) => byte.toString(16).padStart(2, "0")).join("");
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

/**
 * The key for this press of Send (#1979 PR3, ADR 0156 D2). The same content keeps the key it was last
 * sent with, so a double press or a resend after a lost answer replays the one saved record. Changed
 * content gets a new key: the backend replays a known key without reading its content and never attaches
 * an image on a replay, so reusing the key would silently drop the edit. The caller forgets the key only
 * once the submission is saved.
 */
export function keyFor(previous: KeyedContent | null, content: FeedbackContent): KeyedContent {
  return previous !== null && sameContent(previous.content, content) ? previous : { key: newSubmissionKey(), content };
}
