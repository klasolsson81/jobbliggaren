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

    it("consumes only on the continue press", async () => {
      const user = userEvent.setup();
      render(<LinkLandingForm token="link-token" alreadyLoggedIn={true} />);
      expect(consumeLinkMock).not.toHaveBeenCalled();

      await user.click(screen.getByRole("button", { name: "Fortsätt och logga in" }));

      await waitFor(() => expect(consumeLinkMock).toHaveBeenCalledTimes(1));
    });
  });

  it("says the one sentence for a dead link and does NOT render the token again", async () => {
    consumeLinkMock.mockResolvedValue({ kind: "unusable" });
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
