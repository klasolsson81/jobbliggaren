import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

// The three switches for what reaches the notification bell (#2130, ADR 0162; #1663, ADR 0175 D8): one per type, in
// the type's own group, before the e-mail rows. They are delivery choices, never consents: independent of the e-mail
// switch, carrying only their own {enabled}, hidden (not disabled) with the match consent, and followed by one forced
// read of the bell after a CONFIRMED save. The Branschbevakningar group holds its bell switch alone, with no e-mail
// row (security-auditor, 2026-11-20).

const {
  mailMock,
  consentMock,
  topMock,
  summaryMock,
  followConsentMock,
  cadenceMock,
  feedMatchesMock,
  feedFollowedMock,
  feedCriterionMock,
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
  feedCriterionMock: vi.fn(),
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
  updateNotificationFeedCriterionAdsAction: feedCriterionMock,
}));
vi.mock("@/components/shell/notification-feed-provider", () => ({
  useRefreshNotificationFeed: () => refreshBellMock,
}));

import { NotificationsSection } from "./notifications-section";

const MAIL = "E-postnotiser";
const MATCH = "Matcha nya annonser åt mig";
const TOP = "Toppmatchningar via e-post";
const SUMMARY = "Sammanfattning via e-post";
const FOLLOW = "Nya annonser via e-post";
const FEED_MATCHES = "Nya matchningar som notis i appen";
const FEED_FOLLOWED = "Nya annonser som notis i appen";
const FEED_CRITERION = "Nya annonser från branschbevakningar som notis i appen";
const CRITERIA = "Branschbevakningar";
const CADENCE = "Hur ofta";
const PAUSED = "Skickas inte medan e-postnotiser är av.";
const REFUSED = "Ändringen kunde inte sparas. Försök igen.";

interface Saved {
  mail?: boolean;
  match?: boolean;
  follow?: boolean;
  feedMatches?: boolean;
  feedFollowed?: boolean;
  feedCriterion?: boolean;
}

function renderSection({
  mail = true,
  match = false,
  follow = false,
  feedMatches = true,
  feedFollowed = true,
  feedCriterion = true,
}: Saved = {}) {
  return render(
    <NotificationsSection
      initialMailEnabled={mail}
      initialMatchEnabled={match}
      initialTopMatchEnabled
      initialSummaryEnabled
      initialFollowEnabled={follow}
      initialFeedMatchesEnabled={feedMatches}
      initialFeedFollowedAdsEnabled={feedFollowed}
      initialFeedCriterionAdsEnabled={feedCriterion}
      initialCadence="Weekly"
    />,
  );
}

const toggle = (name: string) => screen.getByRole("switch", { name });
const maybeToggle = (name: string) => screen.queryByRole("switch", { name });

beforeEach(() => {
  for (const mock of [
    mailMock,
    consentMock,
    topMock,
    summaryMock,
    followConsentMock,
    cadenceMock,
    feedMatchesMock,
    feedFollowedMock,
    feedCriterionMock,
  ]) {
    mock.mockReset();
    mock.mockResolvedValue({ success: true });
  }
  refreshBellMock.mockReset().mockResolvedValue(undefined);
});

describe("NotificationsSection: the bell switches (#2130, #1663)", () => {
  it("puts each type's bell switch in that type's group, before the e-mail rows", () => {
    renderSection({ match: true });
    const matches = screen.getByRole("region", { name: "Matchningar" });
    const follows = screen.getByRole("region", { name: "Företag du följer" });
    const criteria = screen.getByRole("region", { name: CRITERIA });

    expect(within(matches).getAllByRole("switch")).toHaveLength(4);
    const order = (scope: HTMLElement, labels: string[]) =>
      labels.map((label) => within(scope).getByRole("switch", { name: label })).map((s) => Array.from(scope.querySelectorAll("[role=switch]")).indexOf(s));
    // Matchningar: the consent first, then its bell switch, then the two mails.
    expect(order(matches, [MATCH, FEED_MATCHES, TOP, SUMMARY])).toEqual([0, 1, 2, 3]);
    // Företag du följer: the bell switch before the follow mail (row 8).
    expect(order(follows, [FEED_FOLLOWED, FOLLOW])).toEqual([0, 1]);
    // Branschbevakningar: the bell switch alone.
    expect(within(criteria).getAllByRole("switch")).toHaveLength(1);
    expect(order(criteria, [FEED_CRITERION])).toEqual([0]);
    expect(within(follows).queryByRole("switch", { name: FEED_MATCHES })).toBeNull();
    expect(within(matches).queryByRole("switch", { name: FEED_FOLLOWED })).toBeNull();
    for (const other of [matches, follows]) {
      expect(within(other).queryByRole("switch", { name: FEED_CRITERION })).toBeNull();
    }
  });

  // ADR 0175 D8: the group says only that new ads come as in-app notices. No e-mail row, no description, and no
  // word of consent (security-auditor, 2026-11-20).
  it("gives Branschbevakningar a group of their own after Företag du följer and before the cadence, holding only the bell switch", () => {
    renderSection({ follow: true });
    const follows = screen.getByRole("region", { name: "Företag du följer" });
    const criteria = screen.getByRole("region", { name: CRITERIA });
    const cadence = screen.getByRole("radiogroup", { name: CADENCE });

    expect(follows.compareDocumentPosition(criteria) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(criteria.compareDocumentPosition(cadence) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(criteria).not.toContainElement(cadence);
    expect(within(criteria).getByRole("heading", { level: 3, name: CRITERIA })).toBeInTheDocument();
    // Its whole text is the heading and the switch's label.
    expect(criteria.textContent).toBe(`${CRITERIA}${FEED_CRITERION}`);
  });

  it("starts on for a new user, and carries no description (the label says it all)", () => {
    renderSection({ match: true });

    for (const label of [FEED_MATCHES, FEED_FOLLOWED, FEED_CRITERION]) {
      expect(toggle(label)).toHaveAttribute("aria-checked", "true");
      expect(toggle(label)).not.toHaveAttribute("aria-describedby");
    }
  });

  it("shows the saved value of each switch", () => {
    renderSection({ match: true, feedMatches: false, feedFollowed: false, feedCriterion: false });

    expect(toggle(FEED_MATCHES)).toHaveAttribute("aria-checked", "false");
    expect(toggle(FEED_FOLLOWED)).toHaveAttribute("aria-checked", "false");
    expect(toggle(FEED_CRITERION)).toHaveAttribute("aria-checked", "false");
  });

  // The bell ignores the e-mail pauses (ADR 0164 D1), so its switches neither hide, lock nor explain themselves
  // when e-mail is off.
  it("is unaffected by the e-mail switch: all three stay, enabled, with no status line", () => {
    renderSection({ mail: false, match: true });

    for (const label of [FEED_MATCHES, FEED_FOLLOWED, FEED_CRITERION]) {
      expect(toggle(label)).toBeEnabled();
      expect(toggle(label)).not.toHaveAttribute("aria-describedby");
    }
    // The only status line is the follow mail's own (row 8), which no bell switch may join.
    expect(screen.getAllByText(PAUSED)).toHaveLength(1);
  });

  // No consent and no mail stand in front of the Branschbevakningar switch, so nothing on the card hides or locks it.
  it.each(
    Array.from({ length: 8 }, (_, n) => ({ mail: (n & 1) !== 0, match: (n & 2) !== 0, follow: (n & 4) !== 0 })),
  )("always renders the Branschbevakningar switch with its saved value (mail $mail, match $match, follow $follow)", (saved) => {
    renderSection({ ...saved, feedCriterion: false });

    expect(toggle(FEED_CRITERION)).toBeEnabled();
    expect(toggle(FEED_CRITERION)).toHaveAttribute("aria-checked", "false");
    expect(toggle(FEED_CRITERION)).not.toHaveAttribute("aria-describedby");
  });

  // ADR 0162 D6: a match reaches the bell only with the consent AND the type. With the consent off the switch is
  // unmounted: not disabled with an explanation, not hidden by opacity, and no line about why (ADR 0080 Beslut 5).
  it("unmounts the matches switch while the match consent is off, with nothing in its place", () => {
    renderSection({ match: false });

    expect(maybeToggle(FEED_MATCHES)).not.toBeInTheDocument();
    // Only the followed ads' and the Branschbevakningar's switches carry the words; no line explains the missing one.
    expect(screen.getAllByText(/notis i appen/)).toHaveLength(2);
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

  it("keeps the Branschbevakningar switch's value while the e-mail switch and both consents change, and never writes it", async () => {
    const user = userEvent.setup();
    renderSection({ match: true, follow: true, feedCriterion: false });

    for (const name of [MAIL, MATCH, FOLLOW]) await user.click(toggle(name));
    await waitFor(() => expect(followConsentMock).toHaveBeenCalledTimes(1));

    expect(toggle(FEED_CRITERION)).toHaveAttribute("aria-checked", "false");
    expect(feedCriterionMock).not.toHaveBeenCalled();
  });

  it.each([
    ["the matches switch", FEED_MATCHES, feedMatchesMock, [feedFollowedMock, feedCriterionMock], { match: true }],
    ["the followed ads switch", FEED_FOLLOWED, feedFollowedMock, [feedMatchesMock, feedCriterionMock], { match: true }],
    ["the Branschbevakningar switch", FEED_CRITERION, feedCriterionMock, [feedMatchesMock, feedFollowedMock], { match: true }],
  ] as const)("%s sends exactly {enabled} through its own action, both ways, and no other write", async (_n, label, own, others, saved) => {
    const user = userEvent.setup();
    renderSection(saved);

    await user.click(toggle(label));
    await waitFor(() => expect(own).toHaveBeenCalledTimes(1));
    expect(own).toHaveBeenLastCalledWith({ enabled: false });
    await user.click(toggle(label));
    await waitFor(() => expect(own).toHaveBeenCalledTimes(2));
    expect(own).toHaveBeenLastCalledWith({ enabled: true });

    for (const other of others) expect(other).not.toHaveBeenCalled();
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

  it("asks the bell to read again after a confirmed Branschbevakningar save, and only then", async () => {
    const user = userEvent.setup();
    renderSection();

    await user.click(toggle(FEED_CRITERION));
    await waitFor(() => expect(refreshBellMock).toHaveBeenCalledTimes(1));

    feedCriterionMock.mockResolvedValue({ success: false, error: REFUSED });
    await user.click(toggle(FEED_CRITERION));
    await screen.findByText(REFUSED);
    expect(refreshBellMock).toHaveBeenCalledTimes(1);
  });

  it.each([
    ["the followed ads switch", FEED_FOLLOWED, feedFollowedMock],
    ["the Branschbevakningar switch", FEED_CRITERION, feedCriterionMock],
  ] as const)("%s flips at once, reverts a refused change and says so under itself", async (_n, label, own) => {
    const user = userEvent.setup();
    own.mockResolvedValue({ success: false, error: REFUSED });
    renderSection();

    await user.click(toggle(label));

    expect(await screen.findByText(REFUSED)).toBeInTheDocument();
    expect(toggle(label)).toHaveAttribute("aria-checked", "true");
    expect(toggle(label).closest(".jp-settings-control")).toContainElement(screen.getByText(REFUSED));
    expect(toggle(label)).toHaveAccessibleDescription(REFUSED);
  });

  it("never touches a consent when switched: the match consent and the follow consent keep their values", async () => {
    const user = userEvent.setup();
    renderSection({ match: true, follow: true });

    await user.click(toggle(FEED_MATCHES));
    await user.click(toggle(FEED_FOLLOWED));
    await user.click(toggle(FEED_CRITERION));
    await waitFor(() => expect(feedCriterionMock).toHaveBeenCalledTimes(1));

    expect(toggle(MATCH)).toHaveAttribute("aria-checked", "true");
    expect(toggle(FOLLOW)).toHaveAttribute("aria-checked", "true");
    expect(consentMock).not.toHaveBeenCalled();
    expect(followConsentMock).not.toHaveBeenCalled();
  });
});
