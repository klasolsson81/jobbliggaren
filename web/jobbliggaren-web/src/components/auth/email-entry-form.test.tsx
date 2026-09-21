import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { EmailStepState } from "@/lib/auth/challenge-action-state";
import { EmailEntryForm } from "./email-entry-form";

const requestCodeMock =
  vi.fn<(prev: EmailStepState, formData: FormData) => Promise<EmailStepState>>();

vi.mock("@/lib/auth/challenge-actions", () => ({
  requestCode: (prev: EmailStepState, formData: FormData) => requestCodeMock(prev, formData),
}));

const submitted = (): FormData => requestCodeMock.mock.lastCall![1];

describe("EmailEntryForm", () => {
  beforeEach(() => {
    requestCodeMock.mockReset();
    requestCodeMock.mockResolvedValue(null);
  });

  it("is one labelled address field with its hints wired, no placeholder, and one primary", () => {
    render(<EmailEntryForm next="" />);

    const field = screen.getByLabelText("E-postadress");
    expect(field).toHaveAttribute("type", "email");
    expect(field).toHaveAttribute("autocomplete", "email");
    expect(field).toBeRequired();
    expect(field).toHaveAttribute("aria-required", "true");
    expect(field).not.toHaveAttribute("placeholder");
    expect(field).toHaveAccessibleDescription(
      "Formatet är namn@domän.se Så behandlar vi din e-postadress: integritetspolicyn."
    );
    // The Art. 13 pointer sits where the address is collected.
    expect(screen.getByRole("link", { name: "integritetspolicyn" })).toHaveAttribute(
      "href",
      "/integritet"
    );
    expect(screen.getByRole("button", { name: "Fortsätt" })).toHaveAttribute("type", "submit");
  });

  it("posts the address and carries next through", async () => {
    const user = userEvent.setup();
    render(<EmailEntryForm next="/ansokningar/abc" />);

    await user.type(screen.getByLabelText("E-postadress"), "anna@example.com");
    await user.click(screen.getByRole("button", { name: "Fortsätt" }));

    await waitFor(() => expect(requestCodeMock).toHaveBeenCalledTimes(1));
    expect(submitted().get("email")).toBe("anna@example.com");
    expect(submitted().get("next")).toBe("/ansokningar/abc");
  });

  // jsdom runs constraint validation and blocks a submit like a browser does, so an action that
  // IS reached proves `noValidate`. Remove it and both of these never call the action.
  it("lets the ACTION refuse an empty address, instead of a browser bubble nothing announces", async () => {
    const user = userEvent.setup();
    render(<EmailEntryForm next="" />);

    await user.click(screen.getByRole("button", { name: "Fortsätt" }));

    await waitFor(() => expect(requestCodeMock).toHaveBeenCalledTimes(1));
  });

  it("does not let the browser refuse björn@, which the backend admits", async () => {
    const user = userEvent.setup();
    render(<EmailEntryForm next="" />);

    await user.type(screen.getByLabelText("E-postadress"), "björn@example.se");
    await user.click(screen.getByRole("button", { name: "Fortsätt" }));

    await waitFor(() => expect(requestCodeMock).toHaveBeenCalledTimes(1));
    expect(submitted().get("email")).toBe("björn@example.se");
  });

  it("answers a field error as an alert wired to the field, focuses it and re-seeds what was typed", async () => {
    requestCodeMock.mockResolvedValue({
      error: "Skriv in din e-postadress.",
      channel: "field",
      values: { email: "anna@" },
    });
    const user = userEvent.setup();
    render(<EmailEntryForm next="" />);

    await user.type(screen.getByLabelText("E-postadress"), "anna@");
    await user.click(screen.getByRole("button", { name: "Fortsätt" }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Skriv in din e-postadress.");
    const field = screen.getByLabelText("E-postadress");
    expect(field).toHaveAttribute("aria-invalid", "true");
    expect(field.getAttribute("aria-describedby")).toContain(alert.id);
    expect(field).toHaveValue("anna@");
    await waitFor(() => expect(field).toHaveFocus());
  });

  it("answers a throttle or an outage as a STATUS: no alert, no invalid field, focus on the message", async () => {
    requestCodeMock.mockResolvedValue({
      error: "För många försök. Vänta en stund och försök igen.",
      channel: "status",
      values: { email: "anna@example.com" },
    });
    const user = userEvent.setup();
    render(<EmailEntryForm next="" />);

    await user.type(screen.getByLabelText("E-postadress"), "anna@example.com");
    await user.click(screen.getByRole("button", { name: "Fortsätt" }));

    const status = await screen.findByRole("status");
    expect(status).toHaveTextContent("För många försök. Vänta en stund och försök igen.");
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(screen.getByLabelText("E-postadress")).not.toHaveAttribute("aria-invalid");
    await waitFor(() => expect(status).toHaveFocus());
  });
});
