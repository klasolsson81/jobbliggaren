import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, within } from "@testing-library/react";
import type { NotificationFeed } from "@/lib/dto/notification-feed";
import { NOTIFICATION_FEED_POLL_MS } from "@/lib/hooks/use-notification-feed";

const { markNotificationsReadAction } = vi.hoisted(() => ({ markNotificationsReadAction: vi.fn() }));
vi.mock("@/lib/actions/notification-feed", () => ({ markNotificationsReadAction }));
import { NotificationsBell } from "./notifications-bell";

// 12:00 in Stockholm on Sunday 2026-10-11.
const NOW = new Date("2026-10-11T10:00:00Z");
const AD_1 = "00000000-0000-4000-8000-000000000001";
const AD_2 = "00000000-0000-4000-8000-000000000002";
const MATCH_TOKEN = "2026-10-11T07:14:03.123456+00:00";

function feed(overrides: Partial<NotificationFeed> = {}): NotificationFeed {
  return {
    items: [
      {
        kind: "Match",
        jobAdId: AD_1,
        title: "Utvecklare",
        companyName: "Match AB",
        occurredAt: MATCH_TOKEN,
        isUnread: true,
        grade: "Top",
      },
      {
        kind: "FollowedCompanyAd",
        jobAdId: AD_2,
        title: "Testare",
        companyName: "Följt AB",
        occurredAt: "2026-10-10T15:32:00+00:00",
        isUnread: false,
        grade: null,
      },
    ],
    sources: [
      { kind: "Match", unreadCount: 1, acknowledgedThrough: MATCH_TOKEN },
      { kind: "FollowedCompanyAd", unreadCount: 0, acknowledgedThrough: null },
    ],
    ...overrides,
  };
}

function counts(match: number | null, followed: number | null): NotificationFeed["sources"] {
  return [
    { kind: "Match", unreadCount: match, acknowledgedThrough: match ? MATCH_TOKEN : null },
    { kind: "FollowedCompanyAd", unreadCount: followed, acknowledgedThrough: followed ? MATCH_TOKEN : null },
  ];
}

const ok = (body: NotificationFeed) => Promise.resolve(Response.json(body));
const status = (code: number, headers: Record<string, string> = {}) =>
  Promise.resolve(new Response(null, { status: code, headers }));

let fetchMock: ReturnType<typeof vi.fn>;

async function settle() {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(0);
  });
}

async function renderBell() {
  render(<NotificationsBell />);
  await settle();
}

function bell() {
  return screen.getByRole("button", { name: /^Aviseringar/ });
}

function open() {
  fireEvent.click(bell());
  return screen.getByRole("dialog", { name: "Aviseringar" });
}

async function poll() {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(NOTIFICATION_FEED_POLL_MS);
  });
}

beforeEach(() => {
  vi.useFakeTimers();
  vi.setSystemTime(NOW);
  fetchMock = vi.fn();
  global.fetch = fetchMock as unknown as typeof fetch;
  markNotificationsReadAction.mockReset();
  Object.defineProperty(document, "hidden", { configurable: true, value: false });
});

afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
});

describe("NotificationsBell (#1666)", () => {
  it("shows no badge and a busy list before the first answer", async () => {
    fetchMock.mockReturnValue(new Promise(() => {}));
    await renderBell();

    expect(bell()).toHaveAccessibleName("Aviseringar");
    expect(document.querySelector(".jp-notif-badge")).toBeNull();
    const dialog = open();
    expect(within(dialog).getByRole("list", { hidden: true })).toHaveAttribute("aria-busy", "true");
    expect(within(dialog).getByText("Hämtar aviseringar…")).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledWith("/api/me/notification-feed", expect.objectContaining({ cache: "no-store" }));
  });

  it("shows the exact unread count on the badge and in the button's name", async () => {
    fetchMock.mockReturnValue(ok(feed({ sources: counts(2, 1) })));
    await renderBell();

    expect(document.querySelector(".jp-notif-badge")).toHaveTextContent("3");
    expect(bell()).toHaveAccessibleName("Aviseringar, 3 olästa");
    expect(within(open()).getByText("3 olästa")).toBeInTheDocument();
  });

  it("caps the badge at 99+ but keeps the exact number in the panel", async () => {
    fetchMock.mockReturnValue(ok(feed({ sources: counts(60, 60) })));
    await renderBell();

    expect(document.querySelector(".jp-notif-badge")).toHaveTextContent("99+");
    expect(bell()).toHaveAccessibleName("Aviseringar, fler än 99 olästa");
    expect(within(open()).getByText("120 olästa")).toBeInTheDocument();
  });

  // ADR 0120: a source that refused to count yields no number at all, and no badge would read as 0.
  it("shows an empty pill, no number and no mark-all when a source refused to count", async () => {
    fetchMock.mockReturnValue(ok(feed({ sources: counts(1, null) })));
    await renderBell();

    const badge = document.querySelector(".jp-notif-badge");
    expect(badge).toHaveClass("jp-notif-badge--unknown");
    expect(badge).toHaveTextContent("");
    expect(bell()).toHaveAccessibleName("Aviseringar, olästa finns");
    const dialog = open();
    expect(within(dialog).queryByText(/oläst/)).toBeNull();
    expect(within(dialog).queryByRole("button", { name: "Markera alla som lästa" })).toBeNull();
    expect(within(dialog).getAllByRole("listitem")).toHaveLength(2);
  });

  it("with nothing unread shows no badge, says so, and keeps the read rows", async () => {
    fetchMock.mockReturnValue(
      ok(feed({ items: feed().items.map((i) => ({ ...i, isUnread: false })), sources: counts(0, 0) })),
    );
    await renderBell();

    expect(document.querySelector(".jp-notif-badge")).toBeNull();
    expect(bell()).toHaveAccessibleName("Aviseringar");
    const dialog = open();
    expect(within(dialog).getByText("Inga olästa")).toBeInTheDocument();
    expect(within(dialog).queryByRole("button", { name: "Markera alla som lästa" })).toBeNull();
    expect(within(dialog).queryByText("Ny")).toBeNull();
    expect(within(dialog).getAllByRole("listitem")).toHaveLength(2);
  });

  // ADR 0080 Beslut 5: the invitation to turn notifications on lives in settings only.
  it("describes the empty bell without inviting the user to turn anything on", async () => {
    fetchMock.mockReturnValue(ok({ items: [], sources: counts(0, 0) }));
    await renderBell();

    const dialog = open();
    expect(within(dialog).getByText("Inga aviseringar än")).toBeInTheDocument();
    expect(
      within(dialog).getByText("Nya matchningar och nya annonser från företag du bevakar visas här."),
    ).toBeInTheDocument();
    const hrefs = within(dialog).getAllByRole("link").map((a) => a.getAttribute("href"));
    expect(hrefs).toEqual(["/matchningar", "/foretag/bevakade/nya"]);
  });

  it("renders each row's reason, the Ny tag, a link to the ad and the Swedish time", async () => {
    fetchMock.mockReturnValue(ok(feed()));
    await renderBell();

    const dialog = open();
    const [matchRow, followedRow] = within(dialog).getAllByRole("listitem");
    expect(within(matchRow!).getByText("Toppmatch")).toBeInTheDocument();
    expect(within(matchRow!).getByText("Ny")).toBeInTheDocument();
    expect(within(matchRow!).getByText("idag, kl. 09:14")).toBeInTheDocument();
    const matchLink = within(matchRow!).getByRole("link", { name: "Utvecklare" });
    expect(matchLink).toHaveAttribute("href", `/jobb/${AD_1}`);
    expect(matchLink).toHaveAccessibleDescription(/Toppmatch.*Ny.*idag, kl. 09:14.*Match AB/);
    expect(within(followedRow!).getByText("Bevakar")).toBeInTheDocument();
    expect(within(followedRow!).queryByText("Ny")).toBeNull();
    expect(within(followedRow!).getByText("igår, kl. 17:32")).toBeInTheDocument();
  });

  it("names older rows by weekday within the week, then by date", async () => {
    fetchMock.mockReturnValue(
      ok(
        feed({
          items: [
            { ...feed().items[0]!, occurredAt: "2026-10-07T08:00:00+00:00" },
            { ...feed().items[1]!, occurredAt: "2025-04-03T08:00:00+00:00" },
          ],
        }),
      ),
    );
    await renderBell();

    const dialog = open();
    expect(within(dialog).getByText("onsdag")).toBeInTheDocument();
    expect(within(dialog).getByText(/^3 apr\.? 2025$/)).toBeInTheDocument();
  });

  it("says the read failed, never 'Inga olästa', and retries on request", async () => {
    fetchMock.mockReturnValueOnce(status(502));
    await renderBell();

    expect(document.querySelector(".jp-notif-badge")).toBeNull();
    const dialog = open();
    expect(within(dialog).getByText("Aviseringarna kunde inte hämtas.")).toBeInTheDocument();
    expect(within(dialog).queryByText("Inga olästa")).toBeNull();

    fetchMock.mockReturnValueOnce(ok(feed()));
    fireEvent.click(within(dialog).getByRole("button", { name: "Försök igen" }));
    await settle();
    expect(within(dialog).getByText("1 oläst")).toBeInTheDocument();
  });

  it("keeps the last answer when a later read fails, and says when it is from", async () => {
    fetchMock.mockReturnValueOnce(ok(feed({ sources: counts(2, 0) })));
    await renderBell();
    fetchMock.mockReturnValueOnce(status(502));
    await poll();

    expect(document.querySelector(".jp-notif-badge")).toHaveTextContent("2");
    expect(within(open()).getByText("Listan kunde inte uppdateras och är från kl. 12:00.")).toBeInTheDocument();
  });

  it("stops polling once the session is gone", async () => {
    fetchMock.mockReturnValue(status(401));
    await renderBell();
    await poll();
    await poll();

    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it("waits out Retry-After after a 429", async () => {
    fetchMock.mockReturnValueOnce(ok(feed())).mockReturnValueOnce(status(429, { "Retry-After": "600" }));
    await renderBell();
    await poll();
    expect(fetchMock).toHaveBeenCalledTimes(2);

    fetchMock.mockReturnValue(ok(feed()));
    await poll();
    expect(fetchMock).toHaveBeenCalledTimes(2);
    await poll();
    expect(fetchMock).toHaveBeenCalledTimes(3);
  });

  it("does not poll while the tab is hidden", async () => {
    fetchMock.mockReturnValue(ok(feed()));
    await renderBell();
    Object.defineProperty(document, "hidden", { configurable: true, value: true });
    await poll();

    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it("announces an increase, never the first answer and never a decrease", async () => {
    fetchMock.mockReturnValueOnce(ok(feed({ sources: counts(2, 0) })));
    await renderBell();
    const live = screen.getByRole("status");
    expect(live).toHaveTextContent("");

    fetchMock.mockReturnValueOnce(ok(feed({ sources: counts(2, 2) })));
    await poll();
    expect(live).toHaveTextContent("Du har 4 olästa aviseringar.");

    fetchMock.mockReturnValueOnce(ok(feed({ sources: counts(1, 0) })));
    await poll();
    expect(live).toHaveTextContent("");
  });

  it("marks everything read with the server's tokens, then reads again and moves focus to the heading", async () => {
    fetchMock.mockReturnValueOnce(ok(feed()));
    await renderBell();
    markNotificationsReadAction.mockResolvedValue({ success: true });
    fetchMock.mockReturnValue(
      ok(feed({ items: feed().items.map((i) => ({ ...i, isUnread: false })), sources: counts(0, 0) })),
    );

    const dialog = open();
    fireEvent.click(within(dialog).getByRole("button", { name: "Markera alla som lästa" }));
    await settle();

    expect(markNotificationsReadAction).toHaveBeenCalledWith({ match: MATCH_TOKEN, followedCompanyAd: null });
    expect(within(dialog).getByText("Inga olästa")).toBeInTheDocument();
    expect(within(dialog).getByText("Aviseringar", { selector: ".jp-notif__title" })).toHaveFocus();
    expect(document.querySelector(".jp-notif-badge")).toBeNull();
  });

  it("says so when marking read failed, and keeps the control", async () => {
    fetchMock.mockReturnValue(ok(feed()));
    await renderBell();
    markNotificationsReadAction.mockResolvedValue({ success: false, error: "x" });

    const dialog = open();
    fireEvent.click(within(dialog).getByRole("button", { name: "Markera alla som lästa" }));
    await settle();

    expect(within(dialog).getByRole("alert")).toHaveTextContent(
      "Aviseringarna kunde inte markeras som lästa. Försök igen.",
    );
    expect(within(dialog).getByRole("button", { name: "Markera alla som lästa" })).toBeInTheDocument();
  });

  // ADR 0162: nothing is acknowledged by opening or reading; the Server Action runs on a click only.
  it("never acknowledges by opening the panel or polling with it open", async () => {
    fetchMock.mockReturnValue(ok(feed()));
    await renderBell();

    open();
    await settle();
    expect(markNotificationsReadAction).not.toHaveBeenCalled();
    await poll();
    expect(markNotificationsReadAction).not.toHaveBeenCalled();
  });

  it("reads again when the tab comes back or the panel opens, at most every 30 seconds", async () => {
    fetchMock.mockReturnValue(ok(feed()));
    await renderBell();
    expect(fetchMock).toHaveBeenCalledTimes(1);

    const visible = async () => {
      document.dispatchEvent(new Event("visibilitychange"));
      await settle();
    };
    await act(async () => {
      await vi.advanceTimersByTimeAsync(10_000);
    });
    await visible();
    expect(fetchMock).toHaveBeenCalledTimes(1);

    await act(async () => {
      await vi.advanceTimersByTimeAsync(21_000);
    });
    await visible();
    expect(fetchMock).toHaveBeenCalledTimes(2);

    await act(async () => {
      await vi.advanceTimersByTimeAsync(31_000);
    });
    fireEvent.click(bell());
    await settle();
    expect(fetchMock).toHaveBeenCalledTimes(3);
  });

  it("stops polling on 403 as on 401", async () => {
    fetchMock.mockReturnValue(status(403));
    await renderBell();
    await poll();

    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  // ADR 0120 / design-reviewer: the header's exact number must never sit over "Inga aviseringar än".
  it("says where the unread notifications are when none of them is in the list", async () => {
    fetchMock.mockReturnValue(ok({ items: [], sources: counts(0, 3) }));
    await renderBell();

    const dialog = open();
    expect(within(dialog).getByText("3 olästa")).toBeInTheDocument();
    expect(
      within(dialog).getByText(/^De olästa aviseringarna ryms inte i den här listan\./),
    ).toBeInTheDocument();
    expect(within(dialog).queryByText("Inga aviseringar än")).toBeNull();
    expect(within(dialog).getByRole("button", { name: "Markera alla som lästa" })).toBeInTheDocument();
  });

  it("puts that line above a list of read rows, and says it for a refused count too", async () => {
    const readRows = feed().items.map((i) => ({ ...i, isUnread: false }));
    fetchMock.mockReturnValueOnce(ok({ items: readRows, sources: counts(0, 2) }));
    await renderBell();
    let dialog = open();
    expect(within(dialog).getByText(/^De olästa aviseringarna ryms inte/)).toBeInTheDocument();
    expect(within(dialog).getAllByRole("listitem")).toHaveLength(2);
    fireEvent.click(bell());

    fetchMock.mockReturnValueOnce(ok({ items: [], sources: counts(0, null) }));
    await poll();
    dialog = open();
    expect(within(dialog).getByText(/^De olästa aviseringarna ryms inte/)).toBeInTheDocument();
    expect(within(dialog).queryByText("Inga aviseringar än")).toBeNull();
  });

  it("shows no count line when there has never been a notification", async () => {
    fetchMock.mockReturnValue(ok({ items: [], sources: counts(0, 0) }));
    await renderBell();

    const dialog = open();
    expect(within(dialog).queryByText("Inga olästa")).toBeNull();
    expect(within(dialog).getByText("Inga aviseringar än")).toBeInTheDocument();
  });

  it("keeps Markerar… until the read after acknowledging has landed", async () => {
    fetchMock.mockReturnValueOnce(ok(feed()));
    await renderBell();
    markNotificationsReadAction.mockResolvedValue({ success: true });
    let land: (value: Response) => void = () => {};
    fetchMock.mockReturnValueOnce(new Promise<Response>((resolve) => (land = resolve)));

    const dialog = open();
    fireEvent.click(within(dialog).getByRole("button", { name: "Markera alla som lästa" }));
    await settle();
    expect(within(dialog).getByRole("button", { name: "Markerar…" })).toHaveAttribute("aria-disabled", "true");

    await act(async () => {
      land(Response.json(feed({ items: feed().items.map((i) => ({ ...i, isUnread: false })), sources: counts(0, 0) })));
      await vi.advanceTimersByTimeAsync(0);
    });
    expect(within(dialog).queryByRole("button", { name: /Markera/ })).toBeNull();
    expect(within(dialog).getByText("Inga olästa")).toBeInTheDocument();
  });

  // A poll whose answer was read before /seen must not be the answer the bell keeps afterwards.
  it("replaces a read already in flight when acknowledging", async () => {
    fetchMock.mockReturnValueOnce(ok(feed()));
    await renderBell();
    const signals: AbortSignal[] = [];
    fetchMock.mockImplementationOnce((_url: string, init: RequestInit) => {
      signals.push(init.signal!);
      return new Promise<Response>(() => {});
    });
    await poll();
    markNotificationsReadAction.mockResolvedValue({ success: true });
    fetchMock.mockReturnValueOnce(
      ok(feed({ items: feed().items.map((i) => ({ ...i, isUnread: false })), sources: counts(0, 0) })),
    );

    const dialog = open();
    fireEvent.click(within(dialog).getByRole("button", { name: "Markera alla som lästa" }));
    await settle();

    expect(signals[0]?.aborted).toBe(true);
    expect(fetchMock).toHaveBeenCalledTimes(3);
    expect(within(dialog).getByText("Inga olästa")).toBeInTheDocument();
  });
});
