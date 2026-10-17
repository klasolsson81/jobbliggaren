import { describe, expect, it } from "vitest";
import type { NotificationFeed } from "@/lib/dto/notification-feed";
import { withRowRead } from "./use-notification-feed";

const AD = "7b0a1c52-4a5e-4d0e-9e0a-0b5d9e2f5a11";
const OTHER_AD = "0c6c8c8e-6f1b-4a0e-8b3d-2f1f2f6b0a22";
const FOLLOWED_AD = "3d9f2b6e-1c4a-4f8e-9a7b-5e6d4c3b2a33";

function feed(over: { rowUnread: boolean; matchCount: number | null }): NotificationFeed {
  return {
    items: [
      {
        kind: "Match",
        jobAdId: AD,
        title: "A",
        companyName: "B",
        occurredAt: "2026-10-11T07:14:03.123456+00:00",
        isUnread: over.rowUnread,
        grade: "Top",
      },
      {
        kind: "FollowedCompanyAd",
        jobAdId: FOLLOWED_AD,
        title: "A",
        companyName: "B",
        occurredAt: "2026-10-11T07:00:00.000000+00:00",
        isUnread: true,
        grade: null,
      },
    ],
    sources: [
      { kind: "Match", unreadCount: over.matchCount, acknowledgedThrough: "2026-10-11T07:14:03.123456+00:00" },
      { kind: "FollowedCompanyAd", unreadCount: 1, acknowledgedThrough: "2026-10-11T07:00:00.000000+00:00" },
    ],
  };
}

// The patch is applied to whatever answer is in hand, and a read that started before a confirmed mark can already carry it.
// Lowering that count a second time would make the badge count one too few (ADR 0120).
describe("withRowRead (#2130)", () => {
  it("reads the row and lowers its own source's count by one", () => {
    const patched = withRowRead(feed({ rowUnread: true, matchCount: 3 }), "Match", AD);

    expect(patched.items[0]?.isUnread).toBe(false);
    expect(patched.sources.find((s) => s.kind === "Match")?.unreadCount).toBe(2);
    expect(patched.items[1]?.isUnread).toBe(true);
    expect(patched.sources.find((s) => s.kind === "FollowedCompanyAd")?.unreadCount).toBe(1);
  });

  it("changes nothing when the answer already carries the mark, so a count is never lowered twice", () => {
    const already = feed({ rowUnread: false, matchCount: 2 });

    expect(withRowRead(already, "Match", AD)).toBe(already);
    const once = withRowRead(feed({ rowUnread: true, matchCount: 3 }), "Match", AD);
    expect(withRowRead(once, "Match", AD)).toBe(once);
  });

  it("changes nothing for a row that is not in the list, and never lowers a refused count", () => {
    const listed = feed({ rowUnread: true, matchCount: 3 });
    expect(withRowRead(listed, "Match", OTHER_AD)).toBe(listed);
    expect(withRowRead(listed, "FollowedCompanyAd", AD)).toBe(listed);

    const refused = withRowRead(feed({ rowUnread: true, matchCount: null }), "Match", AD);
    expect(refused.sources.find((s) => s.kind === "Match")?.unreadCount).toBeNull();
  });
});
