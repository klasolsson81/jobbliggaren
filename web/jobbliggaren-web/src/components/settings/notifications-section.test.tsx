import { describe, it, expect, vi, beforeEach, type Mock } from "vitest";
import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ActionResult } from "@/lib/actions/_action-result";

// Server Actions never run in jsdom: mock the eight the section imports.
const {
  mailMock,
  consentMock,
  topMock,
  summaryMock,
  followConsentMock,
  cadenceMock,
  feedMatchesMock,
  feedFollowedMock,
  refreshBellMock,
} = vi.hoisted(() => ({
  mailMock: vi.fn(),
  consentMock: vi.fn(),
  topMock: vi.fn(),
  summaryMock: vi.fn(),
  followConsentMock: vi.fn(),
  cadenceMock: vi.fn(),
  feedMatchesMock: vi.fn(),
  feedFollowedMock: vi.fn(),
  refreshBellMock: vi.fn(),
}));
vi.mock("@/lib/actions/me", () => ({
  updateNotificationMailAction: mailMock,
  updateNotificationConsentAction: consentMock,
  updateTopMatchMailAction: topMock,
  updateMatchSummaryMailAction: summaryMock,
  updateFollowedCompanyNotificationConsentAction: followConsentMock,
  updateDigestCadenceAction: cadenceMock,
  updateNotificationFeedMatchesAction: feedMatchesMock,
  updateNotificationFeedFollowedAdsAction: feedFollowedMock,
}));
// The shell's bell is not rendered here: what the section owes it is one forced read after a confirmed save.
vi.mock("@/components/shell/notification-feed-provider", () => ({
  useRefreshNotificationFeed: () => refreshBellMock,
}));

import { NotificationsSection } from "./notifications-section";
import type { DigestCadence } from "@/lib/dto/me";

const MAIL = "E-postnotiser";
const MATCH = "Matcha nya annonser åt mig";
const TOP = "Toppmatchningar via e-post";
const SUMMARY = "Sammanfattning via e-post";
const FOLLOW = "Nya annonser via e-post";
const CADENCE = "Hur ofta";
const SAVED = /^Sparat \d{2}:\d{2}$/;

// The bound consent texts (ADR 0144 Decision 4): row 7 in security-auditor's variant 3, row 8
// unchanged.
const MATCH_CONSENT =
  "Slår du på det samtycker du till att vi matchar nya annonser mot din profil och mejlar dig toppmatchningar direkt och starka i en sammanfattning. Bra matchningar visas bara i appen. Du kan stänga av det när som helst.";
const FOLLOW_CONSENT =
  "Nya annonser från företag du följer visas alltid i appen. Slår du på det samtycker du till att vi också mejlar dem till dig. Du kan dra tillbaka samtycket när som helst genom att stänga av det här.";
const PAUSED = "Skickas inte medan e-postnotiser är av.";
const TOP_TEXT = "Direkt när en ny annons kommer in.";
const SUMMARY_TEXT = "Nya starka matchningar samlade i ett mejl.";
const CADENCE_HINT =
  "Gäller sammanfattningen och mejlen om företag du följer. De kommer på morgonen, varje dag eller på måndagar.";

interface Saved {
  mail?: boolean;
  match?: boolean;
  top?: boolean;
  summary?: boolean;
  follow?: boolean;
  feedMatches?: boolean;
  feedFollowed?: boolean;
  cadence?: DigestCadence;
}

/** Defaults to a new user: every mail switch on, both consents off, both bell switches on. */
function renderSection({
  mail = true,
  match = false,
  top = true,
  summary = true,
  follow = false,
  feedMatches = true,
  feedFollowed = true,
  cadence = "Weekly",
}: Saved = {}) {
  return render(
    <NotificationsSection
      initialMailEnabled={mail}
      initialMatchEnabled={match}
      initialTopMatchEnabled={top}
      initialSummaryEnabled={summary}
      initialFollowEnabled={follow}
      initialFeedMatchesEnabled={feedMatches}
      initialFeedFollowedAdsEnabled={feedFollowed}
      initialCadence={cadence}
    />,
  );
}

function toggle(name: string) {
  return screen.getByRole("switch", { name });
}

function maybeToggle(name: string) {
  return screen.queryByRole("switch", { name });
}

function selector() {
  return screen.getByRole("radiogroup", { name: CADENCE });
}

function maybeSelector() {
  return screen.queryByRole("radiogroup", { name: CADENCE });
}

/** The unit a control lives in: where its receipt or refusal must land (#1391). */
function unitOf(node: Element): HTMLElement {
  const unit = node.closest<HTMLElement>(".jp-settings-control");
  if (unit === null) throw new Error("The control has no unit.");
  return unit;
}

/** A promise the test settles by hand, so the pending state can be read. */
function held() {
  let settle!: (result: ActionResult) => void;
  const promise = new Promise<ActionResult>((resolve) => {
    settle = resolve;
  });
  return { promise, settle };
}

const ALL_MOCKS = [
  mailMock,
  consentMock,
  topMock,
  summaryMock,
  followConsentMock,
  cadenceMock,
  feedMatchesMock,
  feedFollowedMock,
];

beforeEach(() => {
  for (const mock of ALL_MOCKS) {
    mock.mockReset();
    mock.mockResolvedValue({ success: true });
  }
  refreshBellMock.mockReset().mockResolvedValue(undefined);
});

describe("NotificationsSection — the card", () => {
  it("opens with the e-mail switch, which carries no description", () => {
    const { container } = renderSection();
    expect(screen.getByRole("heading", { level: 2, name: "Notiser" })).toBeInTheDocument();
    const firstControl = container.querySelector("button, input, select, textarea, a[href]");
    expect(firstControl).toBe(toggle(MAIL));
    expect(toggle(MAIL)).not.toHaveAttribute("aria-describedby");
    expect(toggle(MAIL)).toHaveAttribute("aria-checked", "true");
  });

  it("puts the match consent and its mails under Matchningar, and the follow mail under Företag du följer", () => {
    renderSection({ match: true });
    const matches = screen.getByRole("region", { name: "Matchningar" });
    const follows = screen.getByRole("region", { name: "Företag du följer" });
    for (const name of [MATCH, TOP, SUMMARY]) {
      expect(within(matches).getByRole("switch", { name })).toBeInTheDocument();
    }
    expect(within(follows).getByRole("switch", { name: FOLLOW })).toBeInTheDocument();
    expect(within(matches).queryByRole("switch", { name: FOLLOW })).not.toBeInTheDocument();
  });

  it("a new user meets the e-mail switch on, both consents off, and no mail switch or cadence", () => {
    renderSection();
    // E-mail, the match consent, then the followed ads' bell switch (on) and the follow consent. The matches' bell
    // switch waits for the match consent (#2130).
    expect(screen.getAllByRole("switch").map((s) => s.getAttribute("aria-checked"))).toEqual([
      "true",
      "false",
      "true",
      "false",
    ]);
    expect(maybeToggle(TOP)).not.toBeInTheDocument();
    expect(maybeToggle(SUMMARY)).not.toBeInTheDocument();
    expect(maybeSelector()).not.toBeInTheDocument();
    expect(screen.queryByText(PAUSED)).not.toBeInTheDocument();
  });

  it("describes the Toppmatch and summary switches with their own texts", () => {
    renderSection({ match: true });
    expect(toggle(TOP)).toHaveAccessibleDescription(TOP_TEXT);
    expect(toggle(SUMMARY)).toHaveAccessibleDescription(SUMMARY_TEXT);
  });

  it("describes the cadence with its hint", () => {
    renderSection({ follow: true });
    expect(selector()).toHaveAccessibleDescription(CADENCE_HINT);
    expect(screen.getByText(CADENCE_HINT)).toBeVisible();
  });
});

// Every combination of the five saved values. 1, 2 and 5 always render; 3 and 4 while 1 and 2 are
// on; the cadence while 1 ∧ ((2 ∧ 4) ∨ 5); the status line while 1 is off.
const COMBINATIONS = Array.from({ length: 32 }, (_, n) => ({
  mail: (n & 1) !== 0,
  match: (n & 2) !== 0,
  top: (n & 4) !== 0,
  summary: (n & 8) !== 0,
  follow: (n & 16) !== 0,
}));

describe("NotificationsSection — what renders", () => {
  it.each(COMBINATIONS)(
    "mail $mail, match $match, top $top, summary $summary, follow $follow",
    (saved) => {
      renderSection(saved);

      for (const name of [MAIL, MATCH, FOLLOW]) expect(toggle(name)).toBeEnabled();
      expect(toggle(MAIL)).toHaveAttribute("aria-checked", String(saved.mail));
      expect(toggle(MATCH)).toHaveAttribute("aria-checked", String(saved.match));
      expect(toggle(FOLLOW)).toHaveAttribute("aria-checked", String(saved.follow));

      const mailsShown = saved.mail && saved.match;
      if (mailsShown) {
        expect(toggle(TOP)).toHaveAttribute("aria-checked", String(saved.top));
        expect(toggle(SUMMARY)).toHaveAttribute("aria-checked", String(saved.summary));
      } else {
        expect(maybeToggle(TOP)).not.toBeInTheDocument();
        expect(maybeToggle(SUMMARY)).not.toBeInTheDocument();
      }

      const cadenceShown = saved.mail && ((saved.match && saved.summary) || saved.follow);
      expect(maybeSelector() !== null).toBe(cadenceShown);
      expect(screen.queryByText(PAUSED) !== null).toBe(!saved.mail);
    },
  );

  it("Toppmatch on and summary off: both switches show, the cadence does not", () => {
    renderSection({ match: true, top: true, summary: false });
    expect(toggle(TOP)).toHaveAttribute("aria-checked", "true");
    expect(toggle(SUMMARY)).toHaveAttribute("aria-checked", "false");
    expect(maybeSelector()).not.toBeInTheDocument();
  });

  it("the e-mail switch on with every mail type off: the two switches show off, no cadence", () => {
    renderSection({ match: true, top: false, summary: false, follow: false });
    expect(toggle(TOP)).toHaveAttribute("aria-checked", "false");
    expect(toggle(SUMMARY)).toHaveAttribute("aria-checked", "false");
    expect(maybeSelector()).not.toBeInTheDocument();
  });

  it("the e-mail switch on with both consents off: no mail switch, no cadence", () => {
    renderSection({ match: false, follow: false });
    expect(maybeToggle(TOP)).not.toBeInTheDocument();
    expect(maybeSelector()).not.toBeInTheDocument();
  });

  it("shows the cadence through the summary alone", () => {
    renderSection({ match: true, top: false, summary: true, follow: false });
    expect(selector()).toBeInTheDocument();
  });

  it("shows the cadence through the follow mail alone", () => {
    renderSection({ match: false, follow: true });
    expect(selector()).toBeInTheDocument();
  });
});

describe("NotificationsSection — switching reveals and hides", () => {
  it("the e-mail switch off unmounts both mail switches and the cadence, and on brings them back unchanged", async () => {
    const user = userEvent.setup();
    renderSection({ match: true, top: true, summary: false, follow: true, cadence: "Daily" });

    await user.click(toggle(MAIL));
    await waitFor(() => expect(mailMock).toHaveBeenCalledWith({ enabled: false }));
    expect(maybeToggle(TOP)).not.toBeInTheDocument();
    expect(maybeToggle(SUMMARY)).not.toBeInTheDocument();
    expect(maybeSelector()).not.toBeInTheDocument();

    await user.click(toggle(MAIL));
    await waitFor(() => expect(mailMock).toHaveBeenLastCalledWith({ enabled: true }));
    expect(toggle(TOP)).toHaveAttribute("aria-checked", "true");
    expect(toggle(SUMMARY)).toHaveAttribute("aria-checked", "false");
    expect(screen.getByRole("radio", { name: "Dagligen" })).toHaveAttribute("aria-checked", "true");
    for (const mock of [consentMock, topMock, summaryMock, followConsentMock, cadenceMock]) {
      expect(mock).not.toHaveBeenCalled();
    }
  });

  it("the match consent off hides both mail switches, and on shows them with their values", async () => {
    const user = userEvent.setup();
    renderSection({ match: true, top: false, summary: true });

    await user.click(toggle(MATCH));
    await waitFor(() => expect(consentMock).toHaveBeenCalledWith({ enabled: false }));
    expect(maybeToggle(TOP)).not.toBeInTheDocument();
    expect(maybeSelector()).not.toBeInTheDocument();

    await user.click(toggle(MATCH));
    await waitFor(() => expect(consentMock).toHaveBeenLastCalledWith({ enabled: true }));
    expect(toggle(TOP)).toHaveAttribute("aria-checked", "false");
    expect(toggle(SUMMARY)).toHaveAttribute("aria-checked", "true");
    expect(selector()).toBeInTheDocument();
  });

  it("the summary off hides the cadence when no follow mail is on, and on shows it", async () => {
    const user = userEvent.setup();
    renderSection({ match: true });

    await user.click(toggle(SUMMARY));
    await waitFor(() => expect(summaryMock).toHaveBeenCalledWith({ enabled: false }));
    expect(maybeSelector()).not.toBeInTheDocument();

    await user.click(toggle(SUMMARY));
    await waitFor(() => expect(summaryMock).toHaveBeenLastCalledWith({ enabled: true }));
    expect(selector()).toBeInTheDocument();
  });

  it("the follow mail on shows the cadence while the summary is off", async () => {
    const user = userEvent.setup();
    renderSection({ match: true, summary: false });

    await user.click(toggle(FOLLOW));
    await waitFor(() => expect(followConsentMock).toHaveBeenCalledWith({ enabled: true }));
    expect(selector()).toBeInTheDocument();
  });
});

// Security V2: a consent is withdrawn on the surface it was given, with no other choice first.
describe("NotificationsSection — the consent switches while the e-mail switch is off (V2)", () => {
  it.each([
    ["off", false],
    ["on", true],
  ])("keeps both usable and described by their bound texts while they are %s", (_state, on) => {
    renderSection({ mail: false, match: on, follow: on });

    expect(toggle(MATCH)).toBeEnabled();
    expect(toggle(FOLLOW)).toBeEnabled();
    expect(toggle(MATCH)).toHaveAccessibleDescription(MATCH_CONSENT);
    expect(toggle(FOLLOW)).toHaveAccessibleDescription(`${FOLLOW_CONSENT} ${PAUSED}`);
    expect(screen.getByText(MATCH_CONSENT)).toBeVisible();
    expect(screen.getByText(FOLLOW_CONSENT)).toBeVisible();
    expect(screen.getByText(PAUSED)).toBeVisible();
  });

  it("withdraws each consent with one click, through its own action", async () => {
    const user = userEvent.setup();
    renderSection({ mail: false, match: true, follow: true });

    await user.click(toggle(MATCH));
    await user.click(toggle(FOLLOW));

    await waitFor(() => expect(followConsentMock).toHaveBeenCalledTimes(1));
    expect(consentMock).toHaveBeenCalledWith({ enabled: false });
    expect(followConsentMock).toHaveBeenCalledWith({ enabled: false });
    expect(mailMock).not.toHaveBeenCalled();
  });

  it("drops the status line once the e-mail switch is on, leaving row 8 alone", async () => {
    const user = userEvent.setup();
    renderSection({ mail: false, follow: true });

    await user.click(toggle(MAIL));

    await waitFor(() => expect(mailMock).toHaveBeenCalledWith({ enabled: true }));
    expect(screen.queryByText(PAUSED)).not.toBeInTheDocument();
    expect(toggle(FOLLOW)).toHaveAccessibleDescription(FOLLOW_CONSENT);
  });

  // Row 7 stays one text in every state: it is the consent text, and it says nothing about the
  // e-mail switch.
  it.each([
    [false, false],
    [false, true],
    [true, false],
    [true, true],
  ])("describes switch 2 with row 7 alone (e-mail %s, match %s)", (mail, match) => {
    renderSection({ mail, match });
    expect(toggle(MATCH)).toHaveAccessibleDescription(MATCH_CONSENT);
  });
});

const SWITCHES: ReadonlyArray<[string, Mock, Saved]> = [
  [MAIL, mailMock, {}],
  [MATCH, consentMock, {}],
  [TOP, topMock, { match: true }],
  [SUMMARY, summaryMock, { match: true }],
  [FOLLOW, followConsentMock, {}],
];

describe("NotificationsSection — each switch writes its own value", () => {
  it.each(SWITCHES)("%s sends exactly {enabled} through its own action, both ways", async (name, mock, saved) => {
    const user = userEvent.setup();
    renderSection(saved);
    const initial = toggle(name).getAttribute("aria-checked") === "true";

    await user.click(toggle(name));
    await waitFor(() => expect(mock).toHaveBeenCalledTimes(1));
    expect(mock.mock.calls[0]).toStrictEqual([{ enabled: !initial }]);

    await user.click(toggle(name));
    await waitFor(() => expect(mock).toHaveBeenCalledTimes(2));
    expect(mock.mock.calls[1]).toStrictEqual([{ enabled: initial }]);

    for (const other of ALL_MOCKS.filter((candidate) => candidate !== mock)) {
      expect(other).not.toHaveBeenCalled();
    }
  });

  it.each(SWITCHES)("%s flips at once and puts its receipt in its own unit", async (name, mock, saved) => {
    const save = held();
    mock.mockReturnValue(save.promise);
    const user = userEvent.setup();
    renderSection(saved);
    const initial = toggle(name).getAttribute("aria-checked");

    await user.click(toggle(name));
    expect(toggle(name)).not.toHaveAttribute("aria-checked", initial ?? "");
    await act(async () => save.settle({ success: true }));

    const receipt = await screen.findByText(SAVED);
    expect(unitOf(receipt)).toBe(unitOf(toggle(name)));
  });

  // Art. 7(3) for the consents: a change the server did not take must not look taken.
  it.each(SWITCHES)("%s reverts a refused change, with the alert alone in its unit", async (name, mock, saved) => {
    mock.mockResolvedValue({ success: false, error: "nej" });
    const user = userEvent.setup();
    renderSection(saved);
    const initial = toggle(name).getAttribute("aria-checked");

    await user.click(toggle(name));

    await waitFor(() => expect(toggle(name)).toHaveAttribute("aria-checked", initial ?? ""));
    const alert = screen.getByRole("alert");
    expect(alert).toHaveTextContent("nej");
    expect(unitOf(alert)).toBe(unitOf(toggle(name)));
    expect(screen.queryByText(SAVED)).not.toBeInTheDocument();
  });

  it("adds a refusal to the switch's description while it is shown", async () => {
    consentMock.mockResolvedValue({ success: false, error: "nej" });
    const user = userEvent.setup();
    renderSection();

    await user.click(toggle(MATCH));

    await waitFor(() => expect(toggle(MATCH)).toHaveAccessibleDescription(`${MATCH_CONSENT} nej`));
  });

  it("reads a refusal on switch 5 after row 8 and the status line", async () => {
    followConsentMock.mockResolvedValue({ success: false, error: "nej" });
    const user = userEvent.setup();
    renderSection({ mail: false });

    await user.click(toggle(FOLLOW));

    await waitFor(() =>
      expect(toggle(FOLLOW)).toHaveAccessibleDescription(`${FOLLOW_CONSENT} ${PAUSED} nej`),
    );
  });

  it("a refused pause brings the mail switches back with their values", async () => {
    mailMock.mockResolvedValue({ success: false, error: "nej" });
    const user = userEvent.setup();
    renderSection({ match: true, top: false, summary: true });

    await user.click(toggle(MAIL));

    await waitFor(() => expect(toggle(MAIL)).toHaveAttribute("aria-checked", "true"));
    expect(toggle(TOP)).toHaveAttribute("aria-checked", "false");
    expect(toggle(SUMMARY)).toHaveAttribute("aria-checked", "true");
    expect(within(unitOf(toggle(MAIL))).getByRole("alert")).toHaveTextContent("nej");
  });
});

describe("NotificationsSection — the shared cadence", () => {
  it.each([
    ["through the summary alone", { match: true, summary: true, follow: false }],
    ["through the follow mail alone", { match: false, follow: true }],
  ])("sends exactly {cadence} through its own action %s", async (_route, saved) => {
    const user = userEvent.setup();
    renderSection(saved);

    await user.click(screen.getByRole("radio", { name: "Dagligen" }));

    await waitFor(() => expect(cadenceMock).toHaveBeenCalledTimes(1));
    expect(cadenceMock.mock.calls[0]).toStrictEqual([{ cadence: "Daily" }]);
    for (const other of ALL_MOCKS.filter((candidate) => candidate !== cadenceMock)) {
      expect(other).not.toHaveBeenCalled();
    }
  });

  it("puts the receipt under the selector", async () => {
    const user = userEvent.setup();
    renderSection({ follow: true });

    await user.click(screen.getByRole("radio", { name: "Dagligen" }));

    const receipt = await screen.findByText(SAVED);
    expect(unitOf(receipt)).toBe(unitOf(selector()));
  });

  it("reverts a refused cadence and adds the refusal to the hint in its description", async () => {
    cadenceMock.mockResolvedValue({ success: false, error: "nej" });
    const user = userEvent.setup();
    renderSection({ follow: true, cadence: "Weekly" });

    await user.click(screen.getByRole("radio", { name: "Dagligen" }));

    await waitFor(() =>
      expect(screen.getByRole("radio", { name: "Veckovis" })).toHaveAttribute("aria-checked", "true"),
    );
    const alert = screen.getByRole("alert");
    expect(alert).toHaveTextContent("nej");
    expect(unitOf(alert)).toBe(unitOf(selector()));
    expect(selector()).toHaveAccessibleDescription(`${CADENCE_HINT} nej`);
  });

  it("keeps its value while hidden", async () => {
    const user = userEvent.setup();
    renderSection({ follow: true, cadence: "Daily" });

    await user.click(toggle(FOLLOW));
    await waitFor(() => expect(maybeSelector()).not.toBeInTheDocument());
    await user.click(toggle(FOLLOW));

    await waitFor(() =>
      expect(screen.getByRole("radio", { name: "Dagligen" })).toHaveAttribute("aria-checked", "true"),
    );
    expect(cadenceMock).not.toHaveBeenCalled();
  });
});

// Design M8: a receipt or a refusal that lands while its control is unmounted is not shown when the
// control comes back. One that lands while it is shown is.
describe("NotificationsSection — an outcome that lands while its control is hidden", () => {
  it.each([
    [TOP, topMock],
    [SUMMARY, summaryMock],
  ])("drops a refusal on %s that lands while the e-mail switch is off, and keeps the reverted value", async (name, mock) => {
    const save = held();
    mock.mockReturnValue(save.promise);
    const user = userEvent.setup();
    renderSection({ match: true, top: true, summary: true });

    await user.click(toggle(name));
    await user.click(toggle(MAIL));
    await waitFor(() => expect(maybeToggle(name)).not.toBeInTheDocument());
    await act(async () => save.settle({ success: false, error: "nej" }));
    await user.click(toggle(MAIL));

    await waitFor(() => expect(toggle(name)).toBeInTheDocument());
    expect(toggle(name)).toHaveAttribute("aria-checked", "true");
    expect(within(unitOf(toggle(name))).queryByText("nej")).not.toBeInTheDocument();
    expect(toggle(name)).not.toHaveAccessibleDescription(/nej/);
  });

  it.each([
    [TOP, topMock],
    [SUMMARY, summaryMock],
  ])("drops a receipt on %s that lands while the match consent is off", async (name, mock) => {
    const save = held();
    mock.mockReturnValue(save.promise);
    const user = userEvent.setup();
    renderSection({ match: true, top: true, summary: true, follow: true });

    await user.click(toggle(name));
    await user.click(toggle(MATCH));
    await waitFor(() => expect(maybeToggle(name)).not.toBeInTheDocument());
    await act(async () => save.settle({ success: true }));
    await user.click(toggle(MATCH));

    await waitFor(() => expect(toggle(name)).toBeInTheDocument());
    expect(toggle(name)).toHaveAttribute("aria-checked", "false");
    expect(within(unitOf(toggle(name))).queryByText(SAVED)).not.toBeInTheDocument();
  });

  it.each([
    ["refusal", { success: false, error: "nej" } as const],
    ["receipt", { success: true } as const],
  ])("drops a cadence %s that lands after the selector unmounted, then and after it returns", async (_kind, result) => {
    const save = held();
    cadenceMock.mockReturnValue(save.promise);
    const user = userEvent.setup();
    renderSection({ follow: true });

    await user.click(screen.getByRole("radio", { name: "Dagligen" }));
    await user.click(toggle(FOLLOW));
    await waitFor(() => expect(maybeSelector()).not.toBeInTheDocument());
    await act(async () => save.settle(result));
    expect(screen.queryByText("nej")).not.toBeInTheDocument();

    await user.click(toggle(FOLLOW));
    await waitFor(() => expect(followConsentMock).toHaveBeenCalledTimes(2));
    const cadenceUnit = unitOf(selector());
    expect(within(cadenceUnit).queryByText("nej")).not.toBeInTheDocument();
    expect(within(cadenceUnit).queryByText(SAVED)).not.toBeInTheDocument();
    expect(selector()).toHaveAccessibleDescription(CADENCE_HINT);
  });

  it("shows a cadence refusal that lands after the selector left and came back through the summary", async () => {
    const save = held();
    cadenceMock.mockReturnValue(save.promise);
    const user = userEvent.setup();
    renderSection({ match: false, follow: true });

    await user.click(screen.getByRole("radio", { name: "Dagligen" }));
    await user.click(toggle(FOLLOW));
    await user.click(toggle(MATCH));
    await waitFor(() => expect(consentMock).toHaveBeenCalledWith({ enabled: true }));
    await act(async () => save.settle({ success: false, error: "nej" }));

    expect(within(unitOf(selector())).getByRole("alert")).toHaveTextContent("nej");
    expect(selector()).toHaveAccessibleDescription(`${CADENCE_HINT} nej`);
    expect(screen.getByRole("radio", { name: "Veckovis" })).toHaveAttribute("aria-checked", "true");
  });

  // A refused grant removes the selector it had shown; the cadence saved meanwhile keeps its value.
  it("drops the cadence receipt when a refused grant removes the selector again", async () => {
    const grant = held();
    consentMock.mockReturnValue(grant.promise);
    const user = userEvent.setup();
    renderSection();

    await user.click(toggle(MATCH));
    await user.click(screen.getByRole("radio", { name: "Dagligen" }));
    await within(unitOf(selector())).findByText(SAVED);
    await act(async () => grant.settle({ success: false, error: "nej" }));

    await waitFor(() => expect(toggle(MATCH)).toHaveAttribute("aria-checked", "false"));
    expect(maybeSelector()).not.toBeInTheDocument();

    await user.click(toggle(FOLLOW));
    await waitFor(() => expect(followConsentMock).toHaveBeenCalledTimes(1));
    expect(within(unitOf(selector())).queryByText(SAVED)).not.toBeInTheDocument();
    expect(screen.getByRole("radio", { name: "Dagligen" })).toHaveAttribute("aria-checked", "true");
  });
});

// Each control writes only its own value, so none waits for another: a control is locked while its
// own write is pending, and only then (V2 for the consent switches).
describe("NotificationsSection — each control locks only itself", () => {
  it("a pending e-mail switch write leaves every other control free", async () => {
    const save = held();
    mailMock.mockReturnValue(save.promise);
    const user = userEvent.setup();
    renderSection({ mail: false, match: true, follow: true });

    await user.click(toggle(MAIL));

    expect(toggle(MAIL)).toBeDisabled();
    for (const name of [MATCH, TOP, SUMMARY, FOLLOW]) expect(toggle(name)).toBeEnabled();
    for (const radio of within(selector()).getAllByRole("radio")) expect(radio).toBeEnabled();
    await act(async () => save.settle({ success: true }));
    expect(toggle(MAIL)).toBeEnabled();
  });

  it("the selector's save leaves every switch free", async () => {
    const save = held();
    cadenceMock.mockReturnValue(save.promise);
    const user = userEvent.setup();
    renderSection({ match: true, follow: true });

    await user.click(screen.getByRole("radio", { name: "Dagligen" }));

    for (const radio of within(selector()).getAllByRole("radio")) expect(radio).toBeDisabled();
    for (const name of [MAIL, MATCH, TOP, SUMMARY, FOLLOW]) expect(toggle(name)).toBeEnabled();
    await act(async () => save.settle({ success: true }));
  });

  it("keeps another control's receipt while a new write is pending", async () => {
    const user = userEvent.setup();
    renderSection({ match: true, follow: true });
    await user.click(toggle(TOP));
    const topReceipt = await screen.findByText(SAVED);

    const save = held();
    summaryMock.mockReturnValue(save.promise);
    await user.click(toggle(SUMMARY));

    expect(topReceipt).toBeInTheDocument();
    expect(topReceipt).toHaveTextContent(SAVED);
    await act(async () => save.settle({ success: true }));
  });
});

// Chromium drops focus to <body> when the focused control becomes disabled for its write (#1391
// measurement); jsdom keeps it, so these tests move it to <body> by hand. Unmounting the focused
// control drops focus to <body> in both.
describe("NotificationsSection — focus", () => {
  it.each([
    ["saved", { success: true } as const],
    ["refused", { success: false, error: "nej" } as const],
  ])("returns focus to the switch once its write is %s", async (_outcome, result) => {
    const save = held();
    mailMock.mockReturnValue(save.promise);
    const user = userEvent.setup();
    renderSection();

    await user.click(toggle(MAIL));
    document.documentElement.focus();
    expect(document.activeElement).toBe(document.body);
    await act(async () => save.settle(result));

    await waitFor(() => expect(toggle(MAIL)).toHaveFocus());
  });

  it("returns focus to the checked cadence option once its write settles", async () => {
    const save = held();
    cadenceMock.mockReturnValue(save.promise);
    const user = userEvent.setup();
    renderSection({ follow: true });

    await user.click(screen.getByRole("radio", { name: "Dagligen" }));
    document.documentElement.focus();
    await act(async () => save.settle({ success: true }));

    await waitFor(() => expect(screen.getByRole("radio", { name: "Dagligen" })).toHaveFocus());
  });

  it("ends on the switch operated last when two writes overlap", async () => {
    const consentSave = held();
    const followSave = held();
    consentMock.mockReturnValue(consentSave.promise);
    followConsentMock.mockReturnValue(followSave.promise);
    const user = userEvent.setup();
    renderSection();

    await user.click(toggle(MATCH));
    document.documentElement.focus();
    await user.click(toggle(FOLLOW));
    document.documentElement.focus();
    await act(async () => consentSave.settle({ success: true }));
    await act(async () => followSave.settle({ success: true }));

    await waitFor(() => expect(toggle(FOLLOW)).toHaveFocus());
  });

  it("leaves focus alone when the user has moved on", async () => {
    const save = held();
    followConsentMock.mockReturnValue(save.promise);
    const user = userEvent.setup();
    renderSection({ match: true });

    await user.click(toggle(FOLLOW));
    const elsewhere = toggle(TOP);
    elsewhere.focus();
    await act(async () => save.settle({ success: true }));

    expect(elsewhere).toHaveFocus();
  });

  // Design B2: a refused write's revert unmounts the control focus had moved to.
  it("sends focus to the e-mail switch when its refused grant unmounts the focused Toppmatch switch", async () => {
    const save = held();
    mailMock.mockReturnValue(save.promise);
    const user = userEvent.setup();
    renderSection({ mail: false, match: true });

    await user.click(toggle(MAIL));
    toggle(TOP).focus();
    expect(toggle(TOP)).toHaveFocus();
    await act(async () => save.settle({ success: false, error: "nej" }));

    await waitFor(() => expect(maybeToggle(TOP)).not.toBeInTheDocument());
    await waitFor(() => expect(document.activeElement).toBe(toggle(MAIL)));
    expect(document.activeElement).not.toBe(document.body);
  });

  it("sends focus to the match consent when its refused grant unmounts the focused summary switch", async () => {
    const save = held();
    consentMock.mockReturnValue(save.promise);
    const user = userEvent.setup();
    renderSection({ match: false });

    await user.click(toggle(MATCH));
    toggle(SUMMARY).focus();
    await act(async () => save.settle({ success: false, error: "nej" }));

    await waitFor(() => expect(maybeToggle(SUMMARY)).not.toBeInTheDocument());
    await waitFor(() => expect(document.activeElement).toBe(toggle(MATCH)));
  });

  it("sends focus to the follow switch when its refused grant unmounts the focused cadence", async () => {
    const save = held();
    followConsentMock.mockReturnValue(save.promise);
    const user = userEvent.setup();
    renderSection({ follow: false });

    await user.click(toggle(FOLLOW));
    screen.getByRole("radio", { name: "Veckovis" }).focus();
    await act(async () => save.settle({ success: false, error: "nej" }));

    await waitFor(() => expect(maybeSelector()).not.toBeInTheDocument());
    await waitFor(() => expect(document.activeElement).toBe(toggle(FOLLOW)));
  });

  it("sends focus to the e-mail switch even after a write on the Toppmatch switch it removes", async () => {
    const save = held();
    mailMock.mockReturnValue(save.promise);
    const user = userEvent.setup();
    renderSection({ mail: false, match: true });

    await user.click(toggle(MAIL));
    await user.click(toggle(TOP));
    await waitFor(() => expect(topMock).toHaveBeenCalledWith({ enabled: false }));
    expect(toggle(TOP)).toHaveFocus();
    await act(async () => save.settle({ success: false, error: "nej" }));

    await waitFor(() => expect(maybeToggle(TOP)).not.toBeInTheDocument());
    await waitFor(() => expect(document.activeElement).toBe(toggle(MAIL)));
  });

  it("sends focus to the switch whose refusal removed the control, not the one written last", async () => {
    const grant = held();
    consentMock.mockReturnValue(grant.promise);
    const user = userEvent.setup();
    renderSection({ match: false });

    await user.click(toggle(MATCH));
    await user.click(toggle(FOLLOW));
    await waitFor(() => expect(followConsentMock).toHaveBeenCalledWith({ enabled: true }));
    toggle(SUMMARY).focus();
    await act(async () => grant.settle({ success: false, error: "nej" }));

    await waitFor(() => expect(maybeToggle(SUMMARY)).not.toBeInTheDocument());
    await waitFor(() => expect(document.activeElement).toBe(toggle(MATCH)));
  });

  it("leaves focus alone when a removed control was not the one focused", async () => {
    const save = held();
    mailMock.mockReturnValue(save.promise);
    const user = userEvent.setup();
    renderSection({ mail: false, match: true });

    await user.click(toggle(MAIL));
    toggle(TOP).focus();
    toggle(FOLLOW).focus();
    await act(async () => save.settle({ success: false, error: "nej" }));

    await waitFor(() => expect(maybeToggle(TOP)).not.toBeInTheDocument());
    expect(toggle(FOLLOW)).toHaveFocus();
  });
});
