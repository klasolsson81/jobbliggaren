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
  return screen.getByRole("button", { name: /^Notiser/ });
}

function open() {
  fireEvent.click(bell());
  return screen.getByRole("dialog", { name: "Notiser" });
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

    expect(bell()).toHaveAccessibleName("Notiser");
    expect(document.querySelector(".jp-notif-badge")).toBeNull();
    const dialog = open();
    expect(within(dialog).getByRole("list", { hidden: true })).toHaveAttribute("aria-busy", "true");
    expect(within(dialog).getByText("Hämtar notiser…")).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledWith("/api/me/notification-feed", expect.objectContaining({ cache: "no-store" }));
  });

  it("shows the exact unread count on the badge and in the button's name", async () => {
    fetchMock.mockReturnValue(ok(feed({ sources: counts(2, 1) })));
    await renderBell();

    expect(document.querySelector(".jp-notif-badge")).toHaveTextContent("3");
    expect(bell()).toHaveAccessibleName("Notiser, 3 olästa");
    expect(within(open()).getByText("3 olästa")).toBeInTheDocument();
  });

  it("caps the badge at 99+ but keeps the exact number in the panel", async () => {
    fetchMock.mockReturnValue(ok(feed({ sources: counts(60, 60) })));
    await renderBell();

    expect(document.querySelector(".jp-notif-badge")).toHaveTextContent("99+");
    expect(bell()).toHaveAccessibleName("Notiser, fler än 99 olästa");
    expect(within(open()).getByText("120 olästa")).toBeInTheDocument();
  });

  // ADR 0120: a source that refused to count yields no number at all, and no badge would read as 0.
  it("shows an empty pill, no number and no mark-all when a source refused to count", async () => {
    fetchMock.mockReturnValue(ok(feed({ sources: counts(1, null) })));
    await renderBell();

    const badge = document.querySelector(".jp-notif-badge");
    expect(badge).toHaveClass("jp-notif-badge--unknown");
    expect(badge).toHaveTextContent("");
    expect(bell()).toHaveAccessibleName("Notiser, olästa finns");
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
    expect(bell()).toHaveAccessibleName("Notiser");
    const dialog = open();
    expect(within(dialog).getByText("Inga olästa")).toBeInTheDocument();
    expect(within(dialog).queryByRole("button", { name: "Markera alla som lästa" })).toBeNull();
    expect(within(dialog).queryByText("Ny")).toBeNull();
    expect(within(dialog).getAllByRole("listitem")).toHaveLength(2);
  });

  // ADR 0080 Beslut 5: the invitation to turn notifications on lives in settings only, so the empty
  // bell states one fact on one line, with no link and no settings entry (#2130, ADR 0162 D11 as amended).
  it("shows the empty bell as one line with no link and no invitation", async () => {
    fetchMock.mockReturnValue(ok({ items: [], sources: counts(0, 0) }));
    await renderBell();

    const dialog = open();
    expect(within(dialog).getByText("Du har inga notiser.")).toBeInTheDocument();
    expect(dialog.querySelectorAll(".jp-notif__body p")).toHaveLength(1);
    expect(within(dialog).queryAllByRole("link")).toHaveLength(0);
    expect(within(dialog).queryByRole("button", { name: /Markera/ })).toBeNull();
  });

  // AC1 of #2130: the panel holds notifications and nothing else, in every state. The only links are
  // the rows' own, to the ad. The fixed footer to two list pages is gone.
  it.each([
    ["loading", () => new Promise<Response>(() => {})],
    ["failed", () => status(502)],
    ["empty", () => ok({ items: [], sources: counts(0, 0) })],
    ["unread rows", () => ok(feed({ sources: counts(5, 0) }))],
    ["read rows", () => ok(feed({ items: feed().items.map((i) => ({ ...i, isUnread: false })), sources: counts(0, 0) }))],
    ["a refused count beside rows", () => ok(feed({ sources: counts(1, null) }))],
    ["a refused count without rows", () => ok({ items: [], sources: counts(0, null) })],
    ["unread counted but not listed", () => ok({ items: [], sources: counts(0, 3) })],
  ])("holds no link but the rows' own when %s", async (_state, answer) => {
    fetchMock.mockReturnValue(answer());
    await renderBell();

    const dialog = open();
    for (const link of within(dialog).queryAllByRole("link")) {
      expect(link.getAttribute("href")).toMatch(/^\/jobb\/[0-9a-f-]{36}$/);
    }
    expect(dialog.querySelector(".jp-notif__foot")).toBeNull();
    expect(dialog.querySelector(".jp-notif__footlink")).toBeNull();
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
    expect(within(dialog).getByText("Notiserna kunde inte hämtas.")).toBeInTheDocument();
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
    expect(live).toHaveTextContent("Du har 4 olästa notiser.");

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
    expect(within(dialog).getByText("Notiser", { selector: ".jp-notif__title" })).toHaveFocus();
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
      "Notiserna kunde inte markeras som lästa. Försök igen.",
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

  it("keeps the throttle when the tab hides between finished reads", async () => {
    fetchMock.mockReturnValue(ok(feed()));
    await renderBell();
    expect(fetchMock).toHaveBeenCalledTimes(1);

    Object.defineProperty(document, "hidden", { configurable: true, value: true });
    document.dispatchEvent(new Event("visibilitychange"));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(10_000);
    });
    Object.defineProperty(document, "hidden", { configurable: true, value: false });
    document.dispatchEvent(new Event("visibilitychange"));
    await settle();

    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it("aborts a read when the tab hides, and reads again when it comes back", async () => {
    const signals: AbortSignal[] = [];
    fetchMock.mockImplementationOnce((_url: string, init: RequestInit) => {
      signals.push(init.signal!);
      return new Promise<Response>(() => {});
    });
    await renderBell();

    Object.defineProperty(document, "hidden", { configurable: true, value: true });
    document.dispatchEvent(new Event("visibilitychange"));
    expect(signals[0]?.aborted).toBe(true);

    fetchMock.mockReturnValue(ok(feed()));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(10_000);
    });
    Object.defineProperty(document, "hidden", { configurable: true, value: false });
    document.dispatchEvent(new Event("visibilitychange"));
    await settle();

    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(within(open()).getByText("1 oläst")).toBeInTheDocument();
  });

  it("stops polling on 403 as on 401", async () => {
    fetchMock.mockReturnValue(status(403));
    await renderBell();
    await poll();

    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  // ADR 0120: the header's exact number must never sit over "Du har inga notiser.". Unread notifications
  // the list does not carry are counted in one line, with no link: the number is exact or absent.
  it("says how many unread notifications are not in the list, alone when there is no row", async () => {
    fetchMock.mockReturnValue(ok({ items: [], sources: counts(0, 3) }));
    await renderBell();

    const dialog = open();
    expect(within(dialog).getByText("3 olästa")).toBeInTheDocument();
    expect(within(dialog).getByText("3 olästa visas inte i listan.")).toBeInTheDocument();
    expect(within(dialog).queryByText("Du har inga notiser.")).toBeNull();
    expect(within(dialog).getByRole("button", { name: "Markera alla som lästa" })).toBeInTheDocument();
    expect(within(dialog).queryAllByRole("link")).toHaveLength(0);
  });

  it("puts the line after the last row with the exact number, in singular and plural", async () => {
    // feed() shows one unread row. Five are counted, so four are not in the list.
    fetchMock.mockReturnValueOnce(ok(feed({ sources: counts(5, 0) })));
    await renderBell();
    let dialog = open();
    const note = within(dialog).getByText("4 olästa visas inte i listan.");
    expect(within(dialog).getByRole("list").compareDocumentPosition(note) & Node.DOCUMENT_POSITION_FOLLOWING)
      .toBeTruthy();
    expect(note.closest("a")).toBeNull();
    expect(note).not.toHaveAttribute("role");
    fireEvent.click(bell());

    fetchMock.mockReturnValueOnce(ok(feed({ sources: counts(2, 0) })));
    await poll();
    dialog = open();
    expect(within(dialog).getByText("1 oläst visas inte i listan.")).toBeInTheDocument();
  });

  it("shows no such line when every unread row is in the list", async () => {
    fetchMock.mockReturnValue(ok(feed({ sources: counts(1, 0) })));
    await renderBell();

    expect(within(open()).queryByText(/visas inte i listan/)).toBeNull();
  });

  // ADR 0120 clause 3: a refused count has no number, so no line carries one. Beside rows there is no
  // line at all; without rows a number-less line stands alone, never "Du har inga notiser." under an empty pill.
  it("shows a number-less line for a refused count without rows, and none beside rows", async () => {
    fetchMock.mockReturnValueOnce(ok(feed({ sources: counts(1, null) })));
    await renderBell();
    let dialog = open();
    expect(within(dialog).queryByText(/visas inte i listan/)).toBeNull();
    fireEvent.click(bell());

    fetchMock.mockReturnValueOnce(ok({ items: [], sources: counts(0, null) }));
    await poll();
    dialog = open();
    expect(within(dialog).getByText("Olästa notiser visas inte i listan.")).toBeInTheDocument();
    expect(within(dialog).queryByText("Du har inga notiser.")).toBeNull();
    expect(document.querySelector(".jp-notif-badge")).toHaveClass("jp-notif-badge--unknown");
  });

  it("shows no count line when there has never been a notification", async () => {
    fetchMock.mockReturnValue(ok({ items: [], sources: counts(0, 0) }));
    await renderBell();

    const dialog = open();
    expect(within(dialog).queryByText("Inga olästa")).toBeNull();
    expect(within(dialog).getByText("Du har inga notiser.")).toBeInTheDocument();
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
