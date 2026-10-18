import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, within } from "@testing-library/react";
import type { NotificationFeed } from "@/lib/dto/notification-feed";
import { NOTIFICATION_FEED_POLL_MS } from "@/lib/hooks/use-notification-feed";

const { markAllNotificationsReadAction, markNotificationReadAction } = vi.hoisted(() => ({
  markAllNotificationsReadAction: vi.fn(),
  markNotificationReadAction: vi.fn(),
}));
vi.mock("@/lib/actions/notification-feed", () => ({ markAllNotificationsReadAction, markNotificationReadAction }));
import { NotificationFeedProvider } from "./notification-feed-provider";
import { NotificationsBell } from "./notifications-bell";

// Click-to-read in the bell (#2130, ADR 0162 as amended by its PR 2 ADR): a click on an unread row marks THAT row
// read. The dot goes at once, the count falls when the server has agreed, and no read is forced per click.

const NOW = new Date("2026-10-11T10:00:00Z");
const AD_1 = "00000000-0000-4000-8000-000000000001";
const AD_2 = "00000000-0000-4000-8000-000000000002";
const AD_3 = "00000000-0000-4000-8000-000000000003";
const TOKEN = "2026-10-11T07:14:03.123456+00:00";

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

/** Two unread matches and one unread followed ad: three unread in all. */
function threeUnread(): NotificationFeed {
  return {
    items: [
      row("Match", AD_1, "Utvecklare", true),
      row("Match", AD_2, "Tekniker", true),
      row("FollowedCompanyAd", AD_3, "Testare", true),
    ],
    sources: [
      { kind: "Match", unreadCount: 2, acknowledgedThrough: TOKEN },
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

const dots = (scope: ParentNode) => scope.querySelectorAll(".jp-notif__dot").length;
const link = (dialog: HTMLElement, title: string) => within(dialog).getByRole("link", { name: title });

beforeEach(() => {
  vi.useFakeTimers();
  vi.setSystemTime(NOW);
  fetchMock = vi.fn();
  global.fetch = fetchMock as unknown as typeof fetch;
  markAllNotificationsReadAction.mockReset();
  markNotificationReadAction.mockReset();
  Object.defineProperty(document, "hidden", { configurable: true, value: false });
});

afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
});

describe("NotificationsBell: click-to-read (#2130)", () => {
  it("marks the clicked row read at once and lowers the count only when the server has agreed", async () => {
    fetchMock.mockReturnValue(ok(threeUnread()));
    await renderBell();
    let agree: (value: { success: true }) => void = () => {};
    markNotificationReadAction.mockReturnValue(new Promise((resolve) => (agree = resolve)));
    const dialog = open();
    expect(dots(dialog)).toBe(3);

    // A modified click, so the panel stays open and the row can be watched.
    fireEvent.click(link(dialog, "Utvecklare"), { ctrlKey: true });
    await settle();

    expect(markNotificationReadAction).toHaveBeenCalledExactlyOnceWith({ kind: "Match", jobAdId: AD_1 });
    // Dot and weight go at once; the count the header printed is still the server's.
    expect(dots(dialog)).toBe(2);
    expect(link(dialog, "Utvecklare").closest("li")).not.toHaveClass("jp-notif__row--unread");
    expect(within(dialog).getByText("3 olästa")).toBeInTheDocument();
    expect(document.querySelector(".jp-notif-badge")).toHaveTextContent("3");
    // The line for rows the list does not carry is exact: both numbers wait for the server together.
    expect(dialog.querySelector(".jp-notif__notinlist")).toBeNull();

    await act(async () => {
      agree({ success: true });
      await vi.advanceTimersByTimeAsync(0);
    });

    expect(within(dialog).getByText("2 olästa")).toBeInTheDocument();
    expect(document.querySelector(".jp-notif-badge")).toHaveTextContent("2");
    expect(dots(dialog)).toBe(2);
    // No read is forced per click (the read budget is derived from the poll, not from how fast one clicks).
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it("puts the dot back, and the count stays, when the mark is refused", async () => {
    fetchMock.mockReturnValue(ok(threeUnread()));
    await renderBell();
    markNotificationReadAction.mockResolvedValue({ success: false, error: "x" });
    const dialog = open();

    fireEvent.click(link(dialog, "Utvecklare"), { ctrlKey: true });
    await settle();

    expect(dots(dialog)).toBe(3);
    expect(within(dialog).getByText("3 olästa")).toBeInTheDocument();
    expect(link(dialog, "Utvecklare").closest("li")).toHaveClass("jp-notif__row--unread");
  });

  // A Server Action from a stale build rejects instead of answering (ADR 0148). A `void` call has no boundary.
  it("treats a rejected action as a refused mark", async () => {
    fetchMock.mockReturnValue(ok(threeUnread()));
    await renderBell();
    markNotificationReadAction.mockRejectedValue(new Error("UnrecognizedActionError"));
    const dialog = open();

    fireEvent.click(link(dialog, "Utvecklare"), { ctrlKey: true });
    await settle();

    expect(dots(dialog)).toBe(3);
    expect(within(dialog).getByText("3 olästa")).toBeInTheDocument();
  });

  it("does not mark a row that is already read, and does not mark a row twice while the first mark is on its way", async () => {
    fetchMock.mockReturnValue(
      ok({
        items: [row("Match", AD_1, "Utvecklare", true), row("Match", AD_2, "Tekniker", false)],
        sources: [
          { kind: "Match", unreadCount: 1, acknowledgedThrough: TOKEN },
          { kind: "FollowedCompanyAd", unreadCount: 0, acknowledgedThrough: null },
        ],
      }),
    );
    await renderBell();
    markNotificationReadAction.mockReturnValue(new Promise(() => {}));
    const dialog = open();

    fireEvent.click(link(dialog, "Tekniker"), { ctrlKey: true });
    fireEvent.click(link(dialog, "Utvecklare"), { ctrlKey: true });
    fireEvent.click(link(dialog, "Utvecklare"), { ctrlKey: true });
    await settle();

    expect(markNotificationReadAction).toHaveBeenCalledExactlyOnceWith({ kind: "Match", jobAdId: AD_1 });
  });

  it("marks on every way of opening the row: plain, modified and the middle button", async () => {
    fetchMock.mockReturnValue(ok(threeUnread()));
    await renderBell();
    markNotificationReadAction.mockResolvedValue({ success: true });
    const dialog = open();

    // Middle button: fires auxclick and never click. Other buttons are not an opening.
    fireEvent(link(dialog, "Utvecklare"), new MouseEvent("auxclick", { bubbles: true, button: 2 }));
    await settle();
    expect(markNotificationReadAction).not.toHaveBeenCalled();
    fireEvent(link(dialog, "Utvecklare"), new MouseEvent("auxclick", { bubbles: true, button: 1 }));
    await settle();
    expect(markNotificationReadAction).toHaveBeenLastCalledWith({ kind: "Match", jobAdId: AD_1 });

    fireEvent.click(link(dialog, "Tekniker"), { ctrlKey: true });
    await settle();
    expect(markNotificationReadAction).toHaveBeenLastCalledWith({ kind: "Match", jobAdId: AD_2 });
    expect(screen.getByRole("dialog", { name: "Notiser" })).toBeInTheDocument();

    // A plain click closes the panel, and still marks.
    fireEvent.click(link(screen.getByRole("dialog", { name: "Notiser" }), "Testare"));
    await settle();
    expect(markNotificationReadAction).toHaveBeenLastCalledWith({ kind: "FollowedCompanyAd", jobAdId: AD_3 });
    expect(screen.queryByRole("dialog", { name: "Notiser" })).toBeNull();
  });

  it("keeps the confirmed mark when the panel is closed and opened again, without a new read", async () => {
    fetchMock.mockReturnValue(ok(threeUnread()));
    await renderBell();
    markNotificationReadAction.mockResolvedValue({ success: true });
    fireEvent.click(link(open(), "Utvecklare"));
    await settle();

    const dialog = open();

    expect(dots(dialog)).toBe(2);
    expect(within(dialog).getByText("2 olästa")).toBeInTheDocument();
  });

  // A read that was started before the mark was written may be answered before it landed. Such an answer must
  // not put the dot back; a read that starts after the mark needs no patch.
  it("does not let a read that started before the mark bring the dot back", async () => {
    fetchMock.mockReturnValueOnce(ok(threeUnread()));
    await renderBell();
    let land: (value: Response) => void = () => {};
    fetchMock.mockReturnValueOnce(new Promise<Response>((resolve) => (land = resolve)));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(NOTIFICATION_FEED_POLL_MS);
    });
    markNotificationReadAction.mockResolvedValue({ success: true });
    const dialog = open();
    fireEvent.click(link(dialog, "Utvecklare"), { ctrlKey: true });
    await settle();
    expect(within(dialog).getByText("2 olästa")).toBeInTheDocument();

    await act(async () => {
      land(Response.json(threeUnread()));
      await vi.advanceTimersByTimeAsync(0);
    });

    expect(within(dialog).getByText("2 olästa")).toBeInTheDocument();
    expect(dots(dialog)).toBe(2);
  });

  it("takes the server's word once a read that started after the mark lands", async () => {
    fetchMock.mockReturnValueOnce(ok(threeUnread()));
    await renderBell();
    markNotificationReadAction.mockResolvedValue({ success: true });
    const dialog = open();
    fireEvent.click(link(dialog, "Utvecklare"), { ctrlKey: true });
    await settle();
    // The next poll answers after the write: it carries the row as read and 2 unread.
    const afterMark = threeUnread();
    afterMark.items[0] = row("Match", AD_1, "Utvecklare", false);
    afterMark.sources[0] = { kind: "Match", unreadCount: 1, acknowledgedThrough: TOKEN };
    fetchMock.mockReturnValueOnce(ok(afterMark));

    await act(async () => {
      await vi.advanceTimersByTimeAsync(NOTIFICATION_FEED_POLL_MS);
    });

    expect(within(dialog).getByText("2 olästa")).toBeInTheDocument();
    expect(dots(dialog)).toBe(2);
  });

  it("announces a later increase against the number the user actually saw", async () => {
    fetchMock.mockReturnValueOnce(ok(threeUnread()));
    await renderBell();
    markNotificationReadAction.mockResolvedValue({ success: true });
    fireEvent.click(link(open(), "Utvecklare"), { ctrlKey: true });
    await settle();
    // Three unread again: one new notification arrived, so 2 -> 3 is an increase to announce.
    const arrived = threeUnread();
    arrived.items[0] = row("Match", AD_1, "Utvecklare", false);
    arrived.sources[0] = { kind: "Match", unreadCount: 2, acknowledgedThrough: TOKEN };
    fetchMock.mockReturnValueOnce(ok(arrived));

    await act(async () => {
      await vi.advanceTimersByTimeAsync(NOTIFICATION_FEED_POLL_MS);
    });

    expect(screen.getAllByRole("status")[0]!).toHaveTextContent("Du har 3 olästa notiser.");
  });

  it("marks everything with the server's tokens through the bulk action, not row by row", async () => {
    fetchMock.mockReturnValueOnce(ok(threeUnread()));
    await renderBell();
    markAllNotificationsReadAction.mockResolvedValue({ success: true });
    fetchMock.mockReturnValue(
      ok({
        items: threeUnread().items.map((item) => ({ ...item, isUnread: false })),
        sources: [
          { kind: "Match", unreadCount: 0, acknowledgedThrough: TOKEN },
          { kind: "FollowedCompanyAd", unreadCount: 0, acknowledgedThrough: TOKEN },
        ],
      }),
    );

    const dialog = open();
    fireEvent.click(within(dialog).getByRole("button", { name: "Markera alla som lästa" }));
    await settle();

    expect(markAllNotificationsReadAction).toHaveBeenCalledExactlyOnceWith({
      match: TOKEN,
      followedCompanyAd: TOKEN,
      companyWatchCriterionAd: null,
    });
    expect(markNotificationReadAction).not.toHaveBeenCalled();
    expect(within(dialog).getByText("Inga olästa")).toBeInTheDocument();
    expect(dots(dialog)).toBe(0);
  });
});
