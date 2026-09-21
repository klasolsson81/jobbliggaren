import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { LinkStepState } from "@/lib/auth/challenge-action-state";
import { LinkLandingForm } from "./link-landing-form";

const consumeLinkMock =
  vi.fn<(prev: LinkStepState, formData: FormData) => Promise<LinkStepState>>();

vi.mock("@/lib/auth/challenge-actions", () => ({
  consumeLink: (prev: LinkStepState, formData: FormData) => consumeLinkMock(prev, formData),
}));

const UNUSABLE = "Länken går inte att använda. Begär en ny kod på inloggningssidan.";

describe("LinkLandingForm", () => {
  beforeEach(() => {
    consumeLinkMock.mockReset();
    consumeLinkMock.mockResolvedValue(null);
  });

  it("NEVER consumes on mount: mail scanners GET the link, and each would burn it", () => {
    render(<LinkLandingForm token="link-token" alreadyLoggedIn={false} />);

    expect(consumeLinkMock).not.toHaveBeenCalled();
  });

  it("explains the second press and posts the token from a hidden field on it", async () => {
    const user = userEvent.setup();
    const { container } = render(<LinkLandingForm token="link-token" alreadyLoggedIn={false} />);

    expect(
      screen.getByText("Du har öppnat en inloggningslänk från ditt mejl. Länken gäller en gång och i 15 minuter.")
    ).toBeInTheDocument();
    expect(container.querySelector('input[type="hidden"][name="token"]')).toHaveValue("link-token");

    await user.click(screen.getByRole("button", { name: "Logga in" }));

    await waitFor(() => expect(consumeLinkMock).toHaveBeenCalledTimes(1));
    expect(consumeLinkMock.mock.lastCall![1].get("token")).toBe("link-token");
    // The one-button arm never says "replace the session": that is the action's to ask about.
    expect(consumeLinkMock.mock.lastCall![1].has("replaceSession")).toBe(false);
  });

  it("names the arm in the h1", () => {
    const { rerender } = render(<LinkLandingForm token="link-token" alreadyLoggedIn={false} />);
    expect(screen.getByRole("heading", { level: 1, name: "Logga in på Jobbliggaren" })).toBeInTheDocument();

    rerender(<LinkLandingForm token="link-token" alreadyLoggedIn={true} />);
    expect(screen.getByRole("heading", { level: 1, name: "Du är redan inloggad" })).toBeInTheDocument();
  });

  describe("when this browser already holds a session", () => {
    it("says what continuing does, names no address, and asks for a choice between TWO controls", () => {
      const { container } = render(<LinkLandingForm token="link-token" alreadyLoggedIn={true} />);

      expect(
        screen.getByText(/En inloggning är redan aktiv i den här webbläsaren\. Fortsätter du ersätts den/)
      ).toBeInTheDocument();
      expect(container.textContent).not.toMatch(/@/);

      const proceed = screen.getByRole("button", { name: "Fortsätt och logga in" });
      const stay = screen.getByRole("link", { name: "Stanna kvar som inloggad" });
      expect(proceed).toHaveAttribute("type", "submit");
      expect(stay).toHaveAttribute("href", "/oversikt");
      // The one primary comes first; staying is a navigation and consumes nothing.
      expect(proceed.compareDocumentPosition(stay) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
      expect(stay).toHaveAttribute("data-variant", "outline");
    });

    it("consumes only on the continue press, which carries the confirmation", async () => {
      const user = userEvent.setup();
      render(<LinkLandingForm token="link-token" alreadyLoggedIn={true} />);
      expect(consumeLinkMock).not.toHaveBeenCalled();

      await user.click(screen.getByRole("button", { name: "Fortsätt och logga in" }));

      await waitFor(() => expect(consumeLinkMock).toHaveBeenCalledTimes(1));
      expect(consumeLinkMock.mock.lastCall![1].get("replaceSession")).toBe("on");
    });
  });

  // A click in webmail is cross-site, so the GET never saw the session: the ACTION answers
  // `confirm`, and the question is asked then.
  describe("when the action finds a session the GET could not see", () => {
    it("switches to the two-control arm, h1 included, and moves focus to the h1", async () => {
      consumeLinkMock.mockResolvedValueOnce({ kind: "confirm" });
      const user = userEvent.setup();
      render(<LinkLandingForm token="link-token" alreadyLoggedIn={false} />);

      await user.click(screen.getByRole("button", { name: "Logga in" }));

      const heading = await screen.findByRole("heading", { level: 1, name: "Du är redan inloggad" });
      await waitFor(() => expect(heading).toHaveFocus());
      expect(
        screen.getByText(/En inloggning är redan aktiv i den här webbläsaren\. Fortsätter du ersätts den/)
      ).toBeInTheDocument();
      expect(screen.getByRole("link", { name: "Stanna kvar som inloggad" })).toHaveAttribute(
        "href",
        "/oversikt"
      );
      expect(screen.queryByRole("button", { name: "Logga in" })).not.toBeInTheDocument();
    });

    it("sends the confirmation with the second press, and the token again", async () => {
      consumeLinkMock.mockResolvedValueOnce({ kind: "confirm" });
      const user = userEvent.setup();
      render(<LinkLandingForm token="link-token" alreadyLoggedIn={false} />);
      await user.click(screen.getByRole("button", { name: "Logga in" }));

      await user.click(await screen.findByRole("button", { name: "Fortsätt och logga in" }));

      await waitFor(() => expect(consumeLinkMock).toHaveBeenCalledTimes(2));
      const posted = consumeLinkMock.mock.lastCall![1];
      expect(posted.get("replaceSession")).toBe("on");
      expect(posted.get("token")).toBe("link-token");
    });

    it("stays in that arm when the confirmed press hits a retryable error", async () => {
      consumeLinkMock.mockResolvedValueOnce({ kind: "confirm" }).mockResolvedValueOnce({
        kind: "error",
        error: "Det går inte att logga in just nu. Försök igen om några minuter.",
        confirmed: true,
      });
      const user = userEvent.setup();
      render(<LinkLandingForm token="link-token" alreadyLoggedIn={false} />);
      await user.click(screen.getByRole("button", { name: "Logga in" }));
      await user.click(await screen.findByRole("button", { name: "Fortsätt och logga in" }));

      expect(await screen.findByRole("status")).toHaveTextContent("Det går inte att logga in just nu.");
      expect(screen.getByRole("button", { name: "Fortsätt och logga in" })).toBeInTheDocument();
      expect(screen.getByRole("heading", { level: 1, name: "Du är redan inloggad" })).toBeInTheDocument();
    });
  });

  it("says the one sentence for a dead link and does NOT render the token again", async () => {
    consumeLinkMock.mockResolvedValue({ kind: "unusable", confirmed: false });
    const user = userEvent.setup();
    const { container } = render(<LinkLandingForm token="link-token" alreadyLoggedIn={false} />);

    await user.click(screen.getByRole("button", { name: "Logga in" }));

    const message = await screen.findByText(UNUSABLE);
    await waitFor(() => expect(message).toHaveFocus());
    expect(container.querySelector('input[name="token"]')).toBeNull();
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Till inloggningssidan" })).toHaveAttribute(
      "href",
      "/logga-in"
    );
  });

  it("renders a terminal outcome as the panel, in place of the form", async () => {
    consumeLinkMock.mockResolvedValue({
      kind: "outcome",
      result: { outcome: "accountUnavailable" },
      confirmed: false,
    });
    const user = userEvent.setup();
    render(<LinkLandingForm token="link-token" alreadyLoggedIn={false} />);

    await user.click(screen.getByRole("button", { name: "Logga in" }));

    expect(
      await screen.findByRole("heading", {
        level: 2,
        name: "Vi kan inte logga in på den här adressen just nu.",
      })
    ).toBeInTheDocument();
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });

  it("keeps the form on a retryable error, as a status", async () => {
    consumeLinkMock.mockResolvedValue({
      kind: "error",
      error: "Det går inte att logga in just nu. Försök igen om några minuter.",
      confirmed: false,
    });
    const user = userEvent.setup();
    render(<LinkLandingForm token="link-token" alreadyLoggedIn={false} />);

    await user.click(screen.getByRole("button", { name: "Logga in" }));

    const status = await screen.findByRole("status");
    expect(status).toHaveTextContent("Det går inte att logga in just nu.");
    await waitFor(() => expect(status).toHaveFocus());
    expect(screen.getByRole("button", { name: "Logga in" })).toBeInTheDocument();
  });
});
