import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, within } from "@testing-library/react";
import type { NotificationFeed } from "@/lib/dto/notification-feed";
import { renderHook } from "@testing-library/react";
import { NOTIFICATION_FEED_POLL_MS, useNotificationFeed, withRowDismissed } from "@/lib/hooks/use-notification-feed";

const { dismissNotificationAction, markAllNotificationsReadAction, markNotificationReadAction } = vi.hoisted(() => ({
  dismissNotificationAction: vi.fn(),
  markAllNotificationsReadAction: vi.fn(),
  markNotificationReadAction: vi.fn(),
}));
vi.mock("@/lib/actions/notification-feed", () => ({
  dismissNotificationAction,
  markAllNotificationsReadAction,
  markNotificationReadAction,
}));
import { NotificationFeedProvider } from "./notification-feed-provider";
import { NotificationsBell } from "./notifications-bell";

// The X on a bell notification (#2151, ADR 0169): a button beside the row link that takes just that notification out of
// the bell. The row goes at once, the count follows when the server has agreed, and a refusal brings the row back.

const NOW = new Date("2026-10-12T10:00:00Z");
const AD_1 = "00000000-0000-4000-8000-000000000001";
const AD_2 = "00000000-0000-4000-8000-000000000002";
const AD_3 = "00000000-0000-4000-8000-000000000003";
const TOKEN = "2026-10-12T07:14:03.123456+00:00";

function row(kind: "Match" | "FollowedCompanyAd", jobAdId: string, title: string, isUnread: boolean) {
  return {
    kind,
    jobAdId,
    title,
    companyName: `${title} AB`,
    occurredAt: TOKEN,
    isUnread,
    grade: kind === "Match" ? ("Top" as const) : null,
  };
}

/** Utvecklare (unread match), Tekniker (read match), Testare (unread followed ad): two unread in all. */
function threeRows(): NotificationFeed {
  return {
    items: [
      row("Match", AD_1, "Utvecklare", true),
      row("Match", AD_2, "Tekniker", false),
      row("FollowedCompanyAd", AD_3, "Testare", true),
    ],
    sources: [
      { kind: "Match", unreadCount: 1, acknowledgedThrough: TOKEN },
      { kind: "FollowedCompanyAd", unreadCount: 1, acknowledgedThrough: TOKEN },
    ],
  };
}

const ok = (body: NotificationFeed) => Promise.resolve(Response.json(body));
let fetchMock: ReturnType<typeof vi.fn>;

async function settle() {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(0);
  });
}

async function renderBell() {
  render(
    <NotificationFeedProvider>
      <NotificationsBell />
    </NotificationFeedProvider>,
  );
  await settle();
}

function open() {
  fireEvent.click(screen.getByRole("button", { name: /^Notiser/ }));
  return screen.getByRole("dialog", { name: "Notiser" });
}

// The name carries the reason (an ad can be listed once per reason), the title and the company.
const REASON: Record<string, string> = { Utvecklare: "Ny matchning", Tekniker: "Ny matchning", Testare: "Bevakat företag" };
const dismissButton = (dialog: HTMLElement, title: string) =>
  within(dialog).getByRole("button", { name: `Ta bort notisen: ${REASON[title]}, ${title}, ${title} AB` });
const titles = (dialog: HTMLElement) => within(dialog).queryAllByRole("link").map((link) => link.textContent);

beforeEach(() => {
  vi.useFakeTimers();
  vi.setSystemTime(NOW);
  fetchMock = vi.fn();
  global.fetch = fetchMock as unknown as typeof fetch;
  dismissNotificationAction.mockReset();
  markAllNotificationsReadAction.mockReset();
  markNotificationReadAction.mockReset();
  Object.defineProperty(document, "hidden", { configurable: true, value: false });
});

afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
});

describe("withRowDismissed (#2151)", () => {
  it("removes an unread row and lowers its own source's count by one", () => {
    const next = withRowDismissed(threeRows(), "Match", AD_1);

    expect(next.items.map((item) => item.jobAdId)).toEqual([AD_2, AD_3]);
    expect(next.sources).toEqual([
      { kind: "Match", unreadCount: 0, acknowledgedThrough: TOKEN },
      { kind: "FollowedCompanyAd", unreadCount: 1, acknowledgedThrough: TOKEN },
    ]);
  });

  it("removes a read row and lowers no count", () => {
    const next = withRowDismissed(threeRows(), "Match", AD_2);

    expect(next.items.map((item) => item.jobAdId)).toEqual([AD_1, AD_3]);
    expect(next.sources).toEqual(threeRows().sources);
  });

  it("is safe to apply twice and for a row that is not in the list", () => {
    const once = withRowDismissed(threeRows(), "Match", AD_1);

    expect(withRowDismissed(once, "Match", AD_1)).toBe(once);
    expect(withRowDismissed(threeRows(), "FollowedCompanyAd", AD_1).items).toHaveLength(3);
  });

  it("never lowers a refused count (a source that did not count) or one already at zero", () => {
    const refused = threeRows();
    refused.sources = [
      { kind: "Match", unreadCount: null, acknowledgedThrough: TOKEN },
      { kind: "FollowedCompanyAd", unreadCount: 0, acknowledgedThrough: TOKEN },
    ];

    const next = withRowDismissed(refused, "Match", AD_1);

    expect(next.sources).toEqual(refused.sources);
  });
});

describe("NotificationsBell: the X (#2151)", () => {
  it("gives every row a button of its own, named after the notification, that is not inside the row link", async () => {
    fetchMock.mockReturnValue(ok(threeRows()));
    await renderBell();
    const dialog = open();

    for (const title of ["Utvecklare", "Tekniker", "Testare"]) {
      const button = dismissButton(dialog, title);
      expect(button).toHaveAttribute("type", "button");
      expect(button).toHaveClass("jp-icon-btn"); // 44 px at <= 768 px comes with the class (DESIGN.md §5)
      expect(button.closest("a")).toBeNull();
    }
  });

  it("takes the row out at once, calls only the dismiss action, and lowers the count only when the server has agreed", async () => {
    fetchMock.mockReturnValue(ok(threeRows()));
    await renderBell();
    let agree: (value: { success: true }) => void = () => {};
    dismissNotificationAction.mockReturnValue(new Promise((resolve) => (agree = resolve)));
    const dialog = open();
    expect(within(dialog).getByText("2 olästa")).toBeInTheDocument();

    fireEvent.click(dismissButton(dialog, "Utvecklare"));
    await settle();

    expect(dismissNotificationAction).toHaveBeenCalledExactlyOnceWith({ kind: "Match", jobAdId: AD_1 });
    expect(markNotificationReadAction).not.toHaveBeenCalled();
    expect(titles(dialog)).toEqual(["Tekniker", "Testare"]);
    // The count is still the server's, and the line for rows the list does not carry stays exact: no "1 oläst visas inte".
    expect(within(dialog).getByText("2 olästa")).toBeInTheDocument();
    expect(document.querySelector(".jp-notif-badge")).toHaveTextContent("2");
    expect(dialog.querySelector(".jp-notif__notinlist")).toBeNull();
    // The click opened nothing: the panel is still open and no navigation was asked for.
    expect(screen.getByRole("dialog", { name: "Notiser" })).toBeInTheDocument();

    await act(async () => {
      agree({ success: true });
      await vi.advanceTimersByTimeAsync(0);
    });

    expect(titles(dialog)).toEqual(["Tekniker", "Testare"]);
    expect(within(dialog).getByText("1 oläst")).toBeInTheDocument();
    expect(document.querySelector(".jp-notif-badge")).toHaveTextContent("1");
    expect(dialog.querySelector(".jp-notif__notinlist")).toBeNull();
    // No forced read per click: the one read at mount is the only one.
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it("takes a read row out without touching the count", async () => {
    fetchMock.mockReturnValue(ok(threeRows()));
    await renderBell();
    dismissNotificationAction.mockResolvedValue({ success: true });
    const dialog = open();

    fireEvent.click(dismissButton(dialog, "Tekniker"));
    await settle();

    expect(dismissNotificationAction).toHaveBeenCalledExactlyOnceWith({ kind: "Match", jobAdId: AD_2 });
    expect(titles(dialog)).toEqual(["Utvecklare", "Testare"]);
    expect(within(dialog).getByText("2 olästa")).toBeInTheDocument();
    expect(document.querySelector(".jp-notif-badge")).toHaveTextContent("2");
  });

  it("sends a followed ad's dismissal under its own kind", async () => {
    fetchMock.mockReturnValue(ok(threeRows()));
    await renderBell();
    dismissNotificationAction.mockResolvedValue({ success: true });
    const dialog = open();

    fireEvent.click(dismissButton(dialog, "Testare"));
    await settle();

    expect(dismissNotificationAction).toHaveBeenCalledExactlyOnceWith({ kind: "FollowedCompanyAd", jobAdId: AD_3 });
    expect(titles(dialog)).toEqual(["Utvecklare", "Tekniker"]);
  });

  it("brings the row back with a notice when the dismissal is refused, and the count never moved", async () => {
    fetchMock.mockReturnValue(ok(threeRows()));
    await renderBell();
    dismissNotificationAction.mockResolvedValue({ success: false, error: "x" });
    const dialog = open();

    fireEvent.click(dismissButton(dialog, "Utvecklare"));
    await settle();

    expect(titles(dialog)).toEqual(["Utvecklare", "Tekniker", "Testare"]);
    expect(within(dialog).getByRole("alert")).toHaveTextContent("Notisen kunde inte tas bort. Försök igen.");
    expect(within(dialog).getByText("2 olästa")).toBeInTheDocument();
    expect(document.querySelector(".jp-notif-badge")).toHaveTextContent("2");
  });

  it("brings the row back when the call itself fails", async () => {
    fetchMock.mockReturnValue(ok(threeRows()));
    await renderBell();
    dismissNotificationAction.mockRejectedValue(new Error("stale build"));
    const dialog = open();

    fireEvent.click(dismissButton(dialog, "Tekniker"));
    await settle();

    expect(titles(dialog)).toEqual(["Utvecklare", "Tekniker", "Testare"]);
    expect(within(dialog).getByRole("alert")).toBeInTheDocument();
  });

  it("clears the notice when the panel is opened again", async () => {
    fetchMock.mockReturnValue(ok(threeRows()));
    await renderBell();
    dismissNotificationAction.mockResolvedValue({ success: false, error: "x" });
    let dialog = open();
    fireEvent.click(dismissButton(dialog, "Tekniker"));
    await settle();
    expect(within(dialog).getByRole("alert")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: /^Notiser/ })); // close
    dialog = open();

    expect(within(dialog).queryByRole("alert")).toBeNull();
  });

  // The row leaves the DOM at once, so focus moves before it goes (WCAG 2.4.3): never to the document body.
  it("moves focus to the next row's X, else the previous row's, else the panel heading", async () => {
    fetchMock.mockReturnValue(ok(threeRows()));
    await renderBell();
    dismissNotificationAction.mockResolvedValue({ success: true });
    const dialog = open();

    fireEvent.click(dismissButton(dialog, "Utvecklare"));
    await settle();
    expect(dismissButton(dialog, "Tekniker")).toHaveFocus();

    fireEvent.click(dismissButton(dialog, "Testare")); // the last row: the previous row's X
    await settle();
    expect(dismissButton(dialog, "Tekniker")).toHaveFocus();

    fireEvent.click(dismissButton(dialog, "Tekniker")); // the only row left: the heading
    await settle();
    expect(within(dialog).getByText("Notiser", { selector: ".jp-notif__title" })).toHaveFocus();
    expect(within(dialog).getByText("Du har inga notiser.")).toBeInTheDocument();
  });

  it("announces the removal in a polite region that is mounted before it is needed", async () => {
    fetchMock.mockReturnValue(ok(threeRows()));
    await renderBell();
    dismissNotificationAction.mockResolvedValue({ success: true });
    const dialog = open();
    const regions = () => screen.getAllByRole("status");
    expect(regions().map((region) => region.textContent)).not.toContain("Notisen Utvecklare (Ny matchning) är borttagen.");

    fireEvent.click(dismissButton(dialog, "Utvecklare"));
    await settle();

    expect(regions().map((region) => region.textContent)).toContain("Notisen Utvecklare (Ny matchning) är borttagen.");
  });

  // A read that started before the dismissal was confirmed may have been answered before the write landed; its stale row must
  // not put the notification back (the same rule as a confirmed read mark).
  it("does not let a read that started before the confirmation bring the row back", async () => {
    fetchMock.mockReturnValueOnce(ok(threeRows()));
    await renderBell();
    let answer: (response: Response) => void = () => {};
    fetchMock.mockReturnValueOnce(new Promise<Response>((resolve) => (answer = resolve)));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(31_000); // past the 30 s throttle
    });
    const dialog = open(); // starts the second read, which is held
    dismissNotificationAction.mockResolvedValue({ success: true });

    fireEvent.click(dismissButton(dialog, "Utvecklare"));
    await settle();
    expect(titles(dialog)).toEqual(["Tekniker", "Testare"]);

    await act(async () => {
      answer(Response.json(threeRows())); // the answer that predates the write
      await vi.advanceTimersByTimeAsync(0);
    });

    expect(titles(dialog)).toEqual(["Tekniker", "Testare"]);
    expect(within(dialog).getByText("1 oläst")).toBeInTheDocument();
  });
  // The removal has a region of its own: after an X and an Escape, a poll that finds MORE notifications is still announced,
  // and the removal's message neither masks it nor comes back.
  it("does not let the removal message mask a later announcement of new notifications", async () => {
    fetchMock.mockReturnValueOnce(ok(threeRows()));
    await renderBell();
    dismissNotificationAction.mockResolvedValue({ success: true });
    const dialog = open();
    fireEvent.click(dismissButton(dialog, "Utvecklare"));
    await settle();
    expect(screen.getAllByRole("status").map((region) => region.textContent)).toContain("Notisen Utvecklare (Ny matchning) är borttagen.");

    fireEvent.keyDown(document, { key: "Escape" }); // closes the panel without the bell button
    await settle();
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(screen.getAllByRole("status").map((region) => region.textContent)).not.toContain("Notisen Utvecklare (Ny matchning) är borttagen.");

    const more = threeRows();
    more.sources = [
      { kind: "Match", unreadCount: 2, acknowledgedThrough: TOKEN },
      { kind: "FollowedCompanyAd", unreadCount: 1, acknowledgedThrough: TOKEN },
    ];
    fetchMock.mockReturnValueOnce(ok(more));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(NOTIFICATION_FEED_POLL_MS);
    });

    expect(screen.getAllByRole("status").map((region) => region.textContent)).toContain("Du har 3 olästa notiser.");
  });

  it("clears the refusal notice when the panel is closed with Escape", async () => {
    fetchMock.mockReturnValue(ok(threeRows()));
    await renderBell();
    dismissNotificationAction.mockResolvedValue({ success: false, error: "x" });
    const dialog = open();
    fireEvent.click(dismissButton(dialog, "Tekniker"));
    await settle();
    expect(within(dialog).getByRole("alert")).toBeInTheDocument();

    fireEvent.keyDown(document, { key: "Escape" });
    await settle();
    const reopened = open();

    expect(within(reopened).queryByRole("alert")).toBeNull();
  });

  it("tells two notifications of the same ad apart by their reason", async () => {
    const both = threeRows();
    both.items = [row("Match", AD_1, "Utvecklare", true), row("FollowedCompanyAd", AD_1, "Utvecklare", true)];
    fetchMock.mockReturnValue(ok(both));
    await renderBell();
    const dialog = open();

    expect(within(dialog).getByRole("button", { name: "Ta bort notisen: Ny matchning, Utvecklare, Utvecklare AB" })).toBeInTheDocument();
    expect(within(dialog).getByRole("button", { name: "Ta bort notisen: Bevakat företag, Utvecklare, Utvecklare AB" })).toBeInTheDocument();
  });

  it("gives focus back to the restored row's X when a dismissal is refused", async () => {
    fetchMock.mockReturnValue(ok(threeRows()));
    await renderBell();
    dismissNotificationAction.mockResolvedValue({ success: false, error: "x" });
    const dialog = open();

    fireEvent.click(dismissButton(dialog, "Utvecklare"));
    await settle();
    await settle(); // the focus is given back after the restored row is in the DOM

    expect(titles(dialog)).toEqual(["Utvecklare", "Tekniker", "Testare"]);
    expect(dismissButton(dialog, "Utvecklare")).toHaveFocus();
  });

  // A dismissal confirmed BEFORE a read mark that was clicked earlier: the later confirmation must not demote it, or a read
  // that started before both would patch the row back in as read.
  it("keeps a confirmed dismissal when a read mark for the same row is confirmed after it", async () => {
    fetchMock.mockReturnValueOnce(ok(threeRows()));
    await renderBell();
    let answer: (response: Response) => void = () => {};
    fetchMock.mockReturnValueOnce(new Promise<Response>((resolve) => (answer = resolve)));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(31_000);
    });
    const dialog = open(); // starts the second read, held
    let confirmMark: (value: { success: true }) => void = () => {};
    markNotificationReadAction.mockReturnValue(new Promise((resolve) => (confirmMark = resolve)));
    dismissNotificationAction.mockResolvedValue({ success: true });

    fireEvent.click(within(dialog).getByRole("link", { name: "Utvecklare" }), { ctrlKey: true }); // the mark, still on its way
    fireEvent.click(dismissButton(dialog, "Utvecklare"));
    await settle();
    await act(async () => {
      confirmMark({ success: true }); // confirmed AFTER the dismissal
      await vi.advanceTimersByTimeAsync(0);
    });
    await act(async () => {
      answer(Response.json(threeRows())); // the read that predates both
      await vi.advanceTimersByTimeAsync(0);
    });

    expect(titles(dialog)).toEqual(["Tekniker", "Testare"]);
  });
});

describe("useNotificationFeed().dismiss (#2151)", () => {
  it("sends a row that is already on its way once, however many times it is called", async () => {
    fetchMock.mockReturnValue(ok(threeRows()));
    let confirm: (value: { success: true }) => void = () => {};
    dismissNotificationAction.mockReturnValue(new Promise((resolve) => (confirm = resolve)));
    const { result } = renderHook(() => useNotificationFeed());
    await settle();

    let first: Promise<boolean> = Promise.resolve(false);
    let second: Promise<boolean> = Promise.resolve(false);
    act(() => {
      first = result.current.dismiss("Match", AD_1);
      second = result.current.dismiss("Match", AD_1);
    });
    await act(async () => {
      confirm({ success: true });
      await vi.advanceTimersByTimeAsync(0);
    });

    expect(dismissNotificationAction).toHaveBeenCalledTimes(1);
    await expect(first).resolves.toBe(true);
    await expect(second).resolves.toBe(true);
    expect(result.current.pendingDismiss.size).toBe(0);
  });
});
