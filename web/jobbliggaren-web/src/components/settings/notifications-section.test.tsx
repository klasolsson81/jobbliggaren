import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ActionResult } from "@/lib/actions/_action-result";

// Server Actions never run in jsdom: mock the three the section imports.
const { consentMock, followConsentMock, cadenceMock } = vi.hoisted(() => ({
  consentMock: vi.fn(),
  followConsentMock: vi.fn(),
  cadenceMock: vi.fn(),
}));
vi.mock("@/lib/actions/me", () => ({
  updateNotificationConsentAction: consentMock,
  updateFollowedCompanyNotificationConsentAction: followConsentMock,
  updateDigestCadenceAction: cadenceMock,
}));

import { NotificationsSection } from "./notifications-section";
import type { DigestCadence } from "@/lib/dto/me";

const MATCH = "Matcha nya annonser åt mig";
const FOLLOW = "Mejla mig nya annonser från företag jag följer";
const CADENCE = "Sammanfattning via e-post";
const SAVED = /^Sparat \d{2}:\d{2}$/;

// The bound consent texts (ADR 0144 Decision 4, rows 7 and 8), as security-auditor signed them.
const MATCH_CONSENT =
  "Slår du på det samtycker du till att vi varje natt matchar nya annonser mot din profil och mejlar dig toppmatchningar direkt och starka i en sammanfattning. Bra matchningar visas bara i din matchningslista. Du kan stänga av det när som helst.";
const FOLLOW_CONSENT =
  "Nya annonser från företag du följer visas alltid i appen. Slår du på det samtycker du till att vi också mejlar dem till dig. Du kan dra tillbaka samtycket när som helst genom att stänga av det här.";

function renderSection({
  match = false,
  follow = false,
  cadence = "Weekly",
}: { match?: boolean; follow?: boolean; cadence?: DigestCadence } = {}) {
  return render(
    <NotificationsSection
      initialMatchEnabled={match}
      initialFollowEnabled={follow}
      initialCadence={cadence}
    />,
  );
}

/** The group a control lives in: the unit a receipt or refusal must land in (#1391). */
function groupOf(node: Element): HTMLElement | null {
  return node.closest<HTMLElement>(".jp-settings-group");
}

function cadenceRadios() {
  return within(screen.getByRole("radiogroup", { name: CADENCE })).getAllByRole("radio");
}

/** A promise the test settles by hand, so the pending state can be read. */
function held() {
  let settle!: (result: ActionResult) => void;
  const promise = new Promise<ActionResult>((resolve) => {
    settle = resolve;
  });
  return { promise, settle };
}

beforeEach(() => {
  consentMock.mockReset();
  consentMock.mockResolvedValue({ success: true });
  followConsentMock.mockReset();
  followConsentMock.mockResolvedValue({ success: true });
  cadenceMock.mockReset();
  cadenceMock.mockResolvedValue({ success: true });
});

describe("NotificationsSection — the card", () => {
  it("is one card, Notiser, with two switches and one cadence selector (ADR 0087 D2)", () => {
    renderSection();
    expect(screen.getByRole("heading", { level: 2, name: "Notiser" })).toBeInTheDocument();
    expect(screen.getAllByRole("switch")).toHaveLength(2);
    expect(screen.getAllByRole("radiogroup")).toHaveLength(1);
  });

  it("starts with the background-match consent off (Art. 6(1)(a) opt-in)", () => {
    renderSection();
    expect(screen.getByRole("switch", { name: MATCH })).toHaveAttribute("aria-checked", "false");
  });

  it("starts with the followed-company mail consent off (Art. 6(1)(a) opt-in)", () => {
    renderSection();
    expect(screen.getByRole("switch", { name: FOLLOW })).toHaveAttribute("aria-checked", "false");
  });

  // Row 7 and row 8 are visible without a click and are each switch's description, on and off.
  it.each([
    ["off", false],
    ["on", true],
  ])("describes each switch with its bound consent text while %s", (_state, on) => {
    renderSection({ match: on, follow: on });
    expect(screen.getByRole("switch", { name: MATCH })).toHaveAccessibleDescription(MATCH_CONSENT);
    expect(screen.getByRole("switch", { name: FOLLOW })).toHaveAccessibleDescription(FOLLOW_CONSENT);
    expect(screen.getByText(MATCH_CONSENT)).toBeVisible();
    expect(screen.getByText(FOLLOW_CONSENT)).toBeVisible();
  });

  it("closes the cadence while both consents are off, and says why", () => {
    renderSection();
    for (const radio of cadenceRadios()) expect(radio).toBeDisabled();
    const hint = screen.getByText("Slå på någon av notiserna för att välja.");
    expect(screen.getByRole("radiogroup", { name: CADENCE })).toHaveAttribute(
      "aria-describedby",
      hint.id,
    );
  });

  it("pre-fills the saved state, and the hint goes once the cadence is open", () => {
    renderSection({ match: true, cadence: "Daily" });
    expect(screen.getByRole("switch", { name: MATCH })).toHaveAttribute("aria-checked", "true");
    expect(screen.getByRole("radio", { name: "Dagligen" })).toHaveAttribute("aria-checked", "true");
    for (const radio of cadenceRadios()) expect(radio).not.toBeDisabled();
    expect(screen.queryByText("Slå på någon av notiserna för att välja.")).not.toBeInTheDocument();
  });

  it("opens the cadence when only the followed-company mail is on: the cadence drives that mail too", () => {
    renderSection({ follow: true });
    for (const radio of cadenceRadios()) expect(radio).not.toBeDisabled();
  });
});

describe("NotificationsSection — the background-match consent", () => {
  it("turning it on sends exactly {enabled:true} and opens the cadence at once", async () => {
    const user = userEvent.setup();
    renderSection();

    await user.click(screen.getByRole("switch", { name: MATCH }));

    await waitFor(() => expect(consentMock).toHaveBeenCalledTimes(1));
    expect(consentMock).toHaveBeenCalledWith({ enabled: true });
    expect(cadenceMock).not.toHaveBeenCalled();
    for (const radio of cadenceRadios()) expect(radio).not.toBeDisabled();
  });

  it("turning it off (Art. 7(3) withdrawal) sends exactly {enabled:false} and closes the cadence", async () => {
    const user = userEvent.setup();
    renderSection({ match: true, cadence: "Daily" });

    await user.click(screen.getByRole("switch", { name: MATCH }));

    await waitFor(() => expect(consentMock).toHaveBeenCalledTimes(1));
    expect(consentMock).toHaveBeenCalledWith({ enabled: false });
    expect(cadenceMock).not.toHaveBeenCalled();
    for (const radio of cadenceRadios()) expect(radio).toBeDisabled();
  });

  it("puts the receipt under the switch that saved", async () => {
    const user = userEvent.setup();
    renderSection();
    const toggle = screen.getByRole("switch", { name: MATCH });

    await user.click(toggle);

    const receipt = await screen.findByText(SAVED);
    expect(groupOf(receipt)).toBe(groupOf(toggle));
  });

  it("reverts a refused change and puts the alert, alone, under the switch", async () => {
    consentMock.mockResolvedValue({ success: false, error: "nej" });
    const user = userEvent.setup();
    renderSection();
    const toggle = screen.getByRole("switch", { name: MATCH });

    await user.click(toggle);

    await waitFor(() => expect(toggle).toHaveAttribute("aria-checked", "false"));
    const alert = screen.getByRole("alert");
    expect(alert).toHaveTextContent("nej");
    expect(groupOf(alert)).toBe(groupOf(toggle));
    expect(screen.queryByText(SAVED)).not.toBeInTheDocument();
  });

  // Art. 7(3): a withdrawal the server did not take must not look taken. The switch goes back to on.
  it("returns a refused withdrawal to on, with the alert under the switch and no receipt", async () => {
    consentMock.mockResolvedValue({ success: false, error: "nej" });
    const user = userEvent.setup();
    renderSection({ match: true });
    const toggle = screen.getByRole("switch", { name: MATCH });

    await user.click(toggle);

    await waitFor(() => expect(toggle).toHaveAttribute("aria-checked", "true"));
    expect(consentMock).toHaveBeenCalledWith({ enabled: false });
    const alert = screen.getByRole("alert");
    expect(alert).toHaveTextContent("nej");
    expect(groupOf(alert)).toBe(groupOf(toggle));
    expect(screen.queryByText(SAVED)).not.toBeInTheDocument();
  });

  it("adds the refusal to the switch's description while it is shown", async () => {
    consentMock.mockResolvedValue({ success: false, error: "nej" });
    const user = userEvent.setup();
    renderSection();
    const toggle = screen.getByRole("switch", { name: MATCH });

    await user.click(toggle);

    await waitFor(() => expect(toggle).toHaveAccessibleDescription(`${MATCH_CONSENT} nej`));
  });
});

describe("NotificationsSection — the shared cadence", () => {
  // The cadence has its own write, owned by neither consent: it carries no consent value.
  it.each([
    ["the match mail is on", { match: true }],
    ["only the followed-company mail is on", { follow: true }],
  ])("sends exactly {cadence} through its own action when %s, never a consent", async (_state, on) => {
    const user = userEvent.setup();
    renderSection(on);

    await user.click(screen.getByRole("radio", { name: "Dagligen" }));

    await waitFor(() => expect(cadenceMock).toHaveBeenCalledTimes(1));
    expect(cadenceMock).toHaveBeenCalledWith({ cadence: "Daily" });
    expect(consentMock).not.toHaveBeenCalled();
    expect(followConsentMock).not.toHaveBeenCalled();
  });

  it("puts the receipt under the selector", async () => {
    const user = userEvent.setup();
    renderSection({ match: true });

    await user.click(screen.getByRole("radio", { name: "Dagligen" }));

    const receipt = await screen.findByText(SAVED);
    expect(groupOf(receipt)).toBe(groupOf(screen.getByRole("radiogroup", { name: CADENCE })));
  });

  it("reverts a refused cadence and puts the alert under the selector", async () => {
    cadenceMock.mockResolvedValue({ success: false, error: "nej" });
    const user = userEvent.setup();
    renderSection({ match: true, cadence: "Weekly" });

    await user.click(screen.getByRole("radio", { name: "Dagligen" }));

    await waitFor(() =>
      expect(screen.getByRole("radio", { name: "Veckovis" })).toHaveAttribute("aria-checked", "true"),
    );
    const alert = screen.getByRole("alert");
    expect(alert).toHaveTextContent("nej");
    expect(groupOf(alert)).toBe(groupOf(screen.getByRole("radiogroup", { name: CADENCE })));
  });

  it("describes the selector with a refusal while it is open, and drops the refusal when it closes", async () => {
    cadenceMock.mockResolvedValue({ success: false, error: "nej" });
    const user = userEvent.setup();
    renderSection({ follow: true });
    const selector = screen.getByRole("radiogroup", { name: CADENCE });

    await user.click(screen.getByRole("radio", { name: "Dagligen" }));
    await waitFor(() => expect(selector).toHaveAccessibleDescription("nej"));

    await user.click(screen.getByRole("switch", { name: FOLLOW }));

    await waitFor(() => expect(followConsentMock).toHaveBeenCalledWith({ enabled: false }));
    expect(screen.queryByText("nej")).not.toBeInTheDocument();
    expect(selector).toHaveAccessibleDescription("Slå på någon av notiserna för att välja.");
  });

  it.each([
    ["refusal", { success: false, error: "nej" } as const],
    ["receipt", { success: true } as const],
  ])("drops a cadence %s that lands after the selector closed, then and after it reopens", async (_kind, result) => {
    const save = held();
    cadenceMock.mockReturnValue(save.promise);
    const user = userEvent.setup();
    renderSection({ follow: true });
    const selector = screen.getByRole("radiogroup", { name: CADENCE });

    await user.click(screen.getByRole("radio", { name: "Dagligen" }));
    await user.click(screen.getByRole("switch", { name: FOLLOW }));
    await waitFor(() => expect(followConsentMock).toHaveBeenCalledWith({ enabled: false }));
    await act(async () => save.settle(result));

    const cadenceGroup = groupOf(selector)!;
    expect(within(cadenceGroup).queryByText("nej")).not.toBeInTheDocument();
    expect(within(cadenceGroup).queryByText(SAVED)).not.toBeInTheDocument();

    await user.click(screen.getByRole("switch", { name: FOLLOW }));
    await waitFor(() => expect(followConsentMock).toHaveBeenCalledTimes(2));
    expect(within(cadenceGroup).queryByText("nej")).not.toBeInTheDocument();
    expect(within(cadenceGroup).queryByText(SAVED)).not.toBeInTheDocument();
    expect(selector).not.toHaveAccessibleDescription("nej");
  });

  // A refused grant closes the selector it had opened; the cadence saved meanwhile keeps its value,
  // and its receipt goes with the period it belonged to.
  it("drops the cadence receipt when a refused grant closes the selector again", async () => {
    const grant = held();
    consentMock.mockReturnValue(grant.promise);
    const user = userEvent.setup();
    renderSection();
    const selector = screen.getByRole("radiogroup", { name: CADENCE });

    await user.click(screen.getByRole("switch", { name: MATCH }));
    await user.click(screen.getByRole("radio", { name: "Dagligen" }));
    await within(groupOf(selector)!).findByText(SAVED);
    await act(async () => grant.settle({ success: false, error: "nej" }));

    await waitFor(() =>
      expect(screen.getByRole("switch", { name: MATCH })).toHaveAttribute("aria-checked", "false"),
    );
    expect(within(groupOf(selector)!).queryByText(SAVED)).not.toBeInTheDocument();
    expect(screen.getByRole("radio", { name: "Dagligen" })).toHaveAttribute("aria-checked", "true");

    await user.click(screen.getByRole("switch", { name: FOLLOW }));
    await waitFor(() => expect(followConsentMock).toHaveBeenCalledTimes(1));
    expect(within(groupOf(selector)!).queryByText(SAVED)).not.toBeInTheDocument();
  });
});

// Each control writes only its own value, so none has to wait for another: a control is locked
// while its own write is pending, and only then.
describe("NotificationsSection — each control locks only itself", () => {
  it("the match switch's save leaves the selector and the followed-company switch free", async () => {
    const save = held();
    consentMock.mockReturnValue(save.promise);
    const user = userEvent.setup();
    renderSection({ follow: true });

    await user.click(screen.getByRole("switch", { name: MATCH }));

    expect(screen.getByRole("switch", { name: MATCH })).toBeDisabled();
    expect(screen.getByRole("switch", { name: FOLLOW })).not.toBeDisabled();
    for (const radio of cadenceRadios()) expect(radio).not.toBeDisabled();
    await act(async () => save.settle({ success: true }));
    expect(screen.getByRole("switch", { name: MATCH })).not.toBeDisabled();
  });

  it("the selector's save leaves both switches free", async () => {
    const save = held();
    cadenceMock.mockReturnValue(save.promise);
    const user = userEvent.setup();
    renderSection({ match: true });

    await user.click(screen.getByRole("radio", { name: "Dagligen" }));

    for (const radio of cadenceRadios()) expect(radio).toBeDisabled();
    expect(screen.getByRole("switch", { name: MATCH })).not.toBeDisabled();
    expect(screen.getByRole("switch", { name: FOLLOW })).not.toBeDisabled();
    await act(async () => save.settle({ success: true }));
    for (const radio of cadenceRadios()) expect(radio).not.toBeDisabled();
  });

  it("a switch and the selector saving at once each send only their own value", async () => {
    const grant = held();
    consentMock.mockReturnValue(grant.promise);
    const user = userEvent.setup();
    renderSection({ follow: true });

    await user.click(screen.getByRole("switch", { name: MATCH }));
    await user.click(screen.getByRole("radio", { name: "Dagligen" }));

    await waitFor(() => expect(cadenceMock).toHaveBeenCalledTimes(1));
    expect(consentMock).toHaveBeenCalledWith({ enabled: true });
    expect(cadenceMock).toHaveBeenCalledWith({ cadence: "Daily" });
    await act(async () => grant.settle({ success: true }));
  });

  it("keeps another control's receipt while a new write is pending", async () => {
    const user = userEvent.setup();
    renderSection({ match: true, follow: true });
    await user.click(screen.getByRole("radio", { name: "Dagligen" }));
    const cadenceReceipt = await screen.findByText(SAVED);

    const save = held();
    consentMock.mockReturnValue(save.promise);
    await user.click(screen.getByRole("switch", { name: MATCH }));

    expect(cadenceReceipt).toBeInTheDocument();
    expect(cadenceReceipt).toHaveTextContent(SAVED);
    await act(async () => save.settle({ success: true }));
  });
});

describe("NotificationsSection — the followed-company mail consent", () => {
  it("opt-in sends {enabled:true} alone: the cadence has its own write", async () => {
    const user = userEvent.setup();
    renderSection();

    await user.click(screen.getByRole("switch", { name: FOLLOW }));

    await waitFor(() => expect(followConsentMock).toHaveBeenCalledTimes(1));
    expect(followConsentMock).toHaveBeenCalledWith({ enabled: true });
    expect(consentMock).not.toHaveBeenCalled();
    expect(cadenceMock).not.toHaveBeenCalled();
  });

  it("flips optimistically before the server answers, and opens the cadence", async () => {
    const save = held();
    followConsentMock.mockReturnValue(save.promise);
    const user = userEvent.setup();
    renderSection();

    await user.click(screen.getByRole("switch", { name: FOLLOW }));

    expect(screen.getByRole("switch", { name: FOLLOW })).toHaveAttribute("aria-checked", "true");
    for (const radio of cadenceRadios()) expect(radio).not.toBeDisabled();
    await act(async () => save.settle({ success: true }));
    expect(await screen.findByText(SAVED)).toBeInTheDocument();
  });

  it("opt-out (Art. 7(3) withdrawal) sends {enabled:false}", async () => {
    const user = userEvent.setup();
    renderSection({ follow: true });

    await user.click(screen.getByRole("switch", { name: FOLLOW }));

    await waitFor(() => expect(followConsentMock).toHaveBeenCalledTimes(1));
    expect(followConsentMock).toHaveBeenCalledWith({ enabled: false });
  });

  it("puts the receipt under the switch that saved", async () => {
    const user = userEvent.setup();
    renderSection();
    const toggle = screen.getByRole("switch", { name: FOLLOW });

    await user.click(toggle);

    const receipt = await screen.findByText(SAVED);
    expect(groupOf(receipt)).toBe(groupOf(toggle));
  });

  it("reverts a refused change, closes the cadence again, and puts the alert under the switch", async () => {
    followConsentMock.mockResolvedValue({ success: false, error: "nej" });
    const user = userEvent.setup();
    renderSection();
    const toggle = screen.getByRole("switch", { name: FOLLOW });

    await user.click(toggle);

    await waitFor(() => expect(toggle).toHaveAttribute("aria-checked", "false"));
    for (const radio of cadenceRadios()) expect(radio).toBeDisabled();
    const alert = screen.getByRole("alert");
    expect(alert).toHaveTextContent("nej");
    expect(groupOf(alert)).toBe(groupOf(toggle));
  });
});

// Chromium drops focus to <body> when the focused control becomes disabled for its write (#1391
// measurement); jsdom keeps it, so the tests move it to <body> by hand.
describe("NotificationsSection — focus comes back to the control that saved", () => {
  it.each([
    ["saved", { success: true } as const],
    ["refused", { success: false, error: "nej" } as const],
  ])("returns focus to the switch once its write is %s", async (_outcome, result) => {
    const save = held();
    followConsentMock.mockReturnValue(save.promise);
    const user = userEvent.setup();
    renderSection();
    const toggle = screen.getByRole("switch", { name: FOLLOW });

    await user.click(toggle);
    document.documentElement.focus();
    expect(document.activeElement).toBe(document.body);
    await act(async () => save.settle(result));

    await waitFor(() => expect(toggle).toHaveFocus());
  });

  it("returns focus to the checked cadence option once its write settles", async () => {
    const save = held();
    cadenceMock.mockReturnValue(save.promise);
    const user = userEvent.setup();
    renderSection({ match: true });

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

    await user.click(screen.getByRole("switch", { name: MATCH }));
    document.documentElement.focus();
    await user.click(screen.getByRole("switch", { name: FOLLOW }));
    document.documentElement.focus();
    await act(async () => consentSave.settle({ success: true }));
    await act(async () => followSave.settle({ success: true }));

    await waitFor(() => expect(screen.getByRole("switch", { name: FOLLOW })).toHaveFocus());
  });

  it("leaves focus alone when the user has moved on", async () => {
    const save = held();
    followConsentMock.mockReturnValue(save.promise);
    const user = userEvent.setup();
    renderSection({ match: true });

    await user.click(screen.getByRole("switch", { name: FOLLOW }));
    const elsewhere = screen.getByRole("radio", { name: "Dagligen" });
    elsewhere.focus();
    await act(async () => save.settle({ success: true }));

    expect(elsewhere).toHaveFocus();
  });
});

describe("NotificationsSection — arriving under #notiser", () => {
  const scrollIntoView = vi.fn();

  beforeEach(() => {
    scrollIntoView.mockReset();
    Element.prototype.scrollIntoView = scrollIntoView;
  });

  afterEach(() => {
    window.history.replaceState(null, "", window.location.pathname);
  });

  it("brings the card into view when the page opens under its fragment", () => {
    window.history.replaceState(null, "", "#notiser");
    renderSection();

    expect(scrollIntoView).toHaveBeenCalledTimes(1);
    expect(scrollIntoView.mock.contexts[0]).toBe(
      screen.getByRole("heading", { level: 2, name: "Notiser" }).closest("section"),
    );
  });

  it("leaves the page where it is under any other address", () => {
    renderSection();

    expect(scrollIntoView).not.toHaveBeenCalled();
  });
});
