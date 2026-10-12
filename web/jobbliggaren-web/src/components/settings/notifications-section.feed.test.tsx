import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

// The two switches for what reaches the notification bell (#2130, ADR 0162): one per type, in the type's own
// group, before the e-mail rows. They are delivery choices, never consents: independent of the e-mail switch,
// carrying only their own {enabled}, hidden (not disabled) with the match consent, and followed by one forced read
// of the bell after a CONFIRMED save.

const { mailMock, consentMock, topMock, summaryMock, followConsentMock, cadenceMock, feedMatchesMock, feedFollowedMock, refreshBellMock } =
  vi.hoisted(() => ({
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
vi.mock("@/components/shell/notification-feed-provider", () => ({
  useRefreshNotificationFeed: () => refreshBellMock,
}));

import { NotificationsSection } from "./notifications-section";

const MATCH = "Matcha nya annonser åt mig";
const TOP = "Toppmatchningar via e-post";
const SUMMARY = "Sammanfattning via e-post";
const FOLLOW = "Nya annonser via e-post";
const FEED_MATCHES = "Nya matchningar som notis i appen";
const FEED_FOLLOWED = "Nya annonser som notis i appen";
const PAUSED = "Skickas inte medan e-postnotiser är av.";
const REFUSED = "Ändringen kunde inte sparas. Försök igen.";

interface Saved {
  mail?: boolean;
  match?: boolean;
  follow?: boolean;
  feedMatches?: boolean;
  feedFollowed?: boolean;
}

function renderSection({ mail = true, match = false, follow = false, feedMatches = true, feedFollowed = true }: Saved = {}) {
  return render(
    <NotificationsSection
      initialMailEnabled={mail}
      initialMatchEnabled={match}
      initialTopMatchEnabled
      initialSummaryEnabled
      initialFollowEnabled={follow}
      initialFeedMatchesEnabled={feedMatches}
      initialFeedFollowedAdsEnabled={feedFollowed}
      initialCadence="Weekly"
    />,
  );
}

const toggle = (name: string) => screen.getByRole("switch", { name });
const maybeToggle = (name: string) => screen.queryByRole("switch", { name });

beforeEach(() => {
  for (const mock of [mailMock, consentMock, topMock, summaryMock, followConsentMock, cadenceMock, feedMatchesMock, feedFollowedMock]) {
    mock.mockReset();
    mock.mockResolvedValue({ success: true });
  }
  refreshBellMock.mockReset().mockResolvedValue(undefined);
});

describe("NotificationsSection: the bell switches (#2130)", () => {
  it("puts each type's bell switch in that type's group, before the e-mail rows", () => {
    renderSection({ match: true });
    const matches = screen.getByRole("region", { name: "Matchningar" });
    const follows = screen.getByRole("region", { name: "Företag du följer" });

    expect(within(matches).getAllByRole("switch")).toHaveLength(4);
    const order = (scope: HTMLElement, labels: string[]) =>
      labels.map((label) => within(scope).getByRole("switch", { name: label })).map((s) => Array.from(scope.querySelectorAll("[role=switch]")).indexOf(s));
    // Matchningar: the consent first, then its bell switch, then the two mails.
    expect(order(matches, [MATCH, FEED_MATCHES, TOP, SUMMARY])).toEqual([0, 1, 2, 3]);
    // Företag du följer: the bell switch before the follow mail (row 8).
    expect(order(follows, [FEED_FOLLOWED, FOLLOW])).toEqual([0, 1]);
    expect(within(follows).queryByRole("switch", { name: FEED_MATCHES })).toBeNull();
    expect(within(matches).queryByRole("switch", { name: FEED_FOLLOWED })).toBeNull();
  });

  it("starts on for a new user, and carries no description (the label says it all)", () => {
    renderSection({ match: true });

    for (const label of [FEED_MATCHES, FEED_FOLLOWED]) {
      expect(toggle(label)).toHaveAttribute("aria-checked", "true");
      expect(toggle(label)).not.toHaveAttribute("aria-describedby");
    }
  });

  it("shows the saved value of each switch", () => {
    renderSection({ match: true, feedMatches: false, feedFollowed: false });

    expect(toggle(FEED_MATCHES)).toHaveAttribute("aria-checked", "false");
    expect(toggle(FEED_FOLLOWED)).toHaveAttribute("aria-checked", "false");
  });

  // The bell ignores the e-mail pauses (ADR 0164 D1), so its switches neither hide, lock nor explain themselves
  // when e-mail is off.
  it("is unaffected by the e-mail switch: both stay, enabled, with no status line", () => {
    renderSection({ mail: false, match: true });

    expect(toggle(FEED_MATCHES)).toBeEnabled();
    expect(toggle(FEED_FOLLOWED)).toBeEnabled();
    expect(toggle(FEED_MATCHES)).not.toHaveAttribute("aria-describedby");
    expect(toggle(FEED_FOLLOWED)).not.toHaveAttribute("aria-describedby");
    // The only status line is the follow mail's own (row 8), which the bell switch must not join.
    expect(screen.getAllByText(PAUSED)).toHaveLength(1);
  });

  // ADR 0162 D6: a match reaches the bell only with the consent AND the type. With the consent off the switch is
  // unmounted: not disabled with an explanation, not hidden by opacity, and no line about why (ADR 0080 Beslut 5).
  it("unmounts the matches switch while the match consent is off, with nothing in its place", () => {
    renderSection({ match: false });

    expect(maybeToggle(FEED_MATCHES)).not.toBeInTheDocument();
    // Only the followed ads' switch carries the words; no line explains the missing one.
    expect(screen.getAllByText(/notis i appen/)).toHaveLength(1);
    expect(toggle(MATCH)).toBeEnabled();
  });

  it("keeps the matches switch's saved value across the consent going off and on", async () => {
    const user = userEvent.setup();
    renderSection({ match: true, feedMatches: false });

    await user.click(toggle(MATCH));
    await waitFor(() => expect(maybeToggle(FEED_MATCHES)).not.toBeInTheDocument());
    await user.click(toggle(MATCH));

    await waitFor(() => expect(toggle(FEED_MATCHES)).toBeInTheDocument());
    expect(toggle(FEED_MATCHES)).toHaveAttribute("aria-checked", "false");
    expect(feedMatchesMock).not.toHaveBeenCalled();
  });

  it.each([
    ["the matches switch", FEED_MATCHES, feedMatchesMock, feedFollowedMock, { match: true }],
    ["the followed ads switch", FEED_FOLLOWED, feedFollowedMock, feedMatchesMock, { match: true }],
  ] as const)("%s sends exactly {enabled} through its own action, both ways, and no other write", async (_n, label, own, other, saved) => {
    const user = userEvent.setup();
    renderSection(saved);

    await user.click(toggle(label));
    await waitFor(() => expect(own).toHaveBeenCalledTimes(1));
    expect(own).toHaveBeenLastCalledWith({ enabled: false });
    await user.click(toggle(label));
    await waitFor(() => expect(own).toHaveBeenCalledTimes(2));
    expect(own).toHaveBeenLastCalledWith({ enabled: true });

    expect(other).not.toHaveBeenCalled();
    for (const consentOrMail of [consentMock, followConsentMock, mailMock, topMock, summaryMock, cadenceMock]) {
      expect(consentOrMail).not.toHaveBeenCalled();
    }
  });

  it("asks the bell to read again after a confirmed save, and only then", async () => {
    const user = userEvent.setup();
    renderSection({ match: true });

    await user.click(toggle(FEED_FOLLOWED));
    await waitFor(() => expect(refreshBellMock).toHaveBeenCalledTimes(1));
    await user.click(toggle(FEED_MATCHES));
    await waitFor(() => expect(refreshBellMock).toHaveBeenCalledTimes(2));

    feedFollowedMock.mockResolvedValue({ success: false, error: REFUSED });
    await user.click(toggle(FEED_FOLLOWED));
    await screen.findByText(REFUSED);
    expect(refreshBellMock).toHaveBeenCalledTimes(2);
  });

  it("flips at once, reverts a refused change and says so under the switch", async () => {
    const user = userEvent.setup();
    feedFollowedMock.mockResolvedValue({ success: false, error: REFUSED });
    renderSection();

    await user.click(toggle(FEED_FOLLOWED));

    expect(await screen.findByText(REFUSED)).toBeInTheDocument();
    expect(toggle(FEED_FOLLOWED)).toHaveAttribute("aria-checked", "true");
    expect(toggle(FEED_FOLLOWED).closest(".jp-settings-control")).toContainElement(screen.getByText(REFUSED));
  });

  it("never touches a consent when switched: the match consent and the follow consent keep their values", async () => {
    const user = userEvent.setup();
    renderSection({ match: true, follow: true });

    await user.click(toggle(FEED_MATCHES));
    await user.click(toggle(FEED_FOLLOWED));
    await waitFor(() => expect(feedFollowedMock).toHaveBeenCalledTimes(1));

    expect(toggle(MATCH)).toHaveAttribute("aria-checked", "true");
    expect(toggle(FOLLOW)).toHaveAttribute("aria-checked", "true");
    expect(consentMock).not.toHaveBeenCalled();
    expect(followConsentMock).not.toHaveBeenCalled();
  });
});
