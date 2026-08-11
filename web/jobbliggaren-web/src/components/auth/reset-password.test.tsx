import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ResetPasswordActionState } from "@/lib/actions/reset-password";

// #1171 — the reset island. Two invariants here are load-bearing and easy to regress:
//   · the POST fires only on an explicit submit, never on mount (mail scanners GET this URL, and a
//     reset that ran on load would spend the single-use token before the user saw the form);
//   · on error the form STAYS MOUNTED, because Identity verifies the token before validating the
//     password, so a rejected password leaves the same link usable.
// Assertions are literal Swedish (the test renderer wraps renders in the sv catalogue).

const actionMock = vi.fn<() => Promise<ResetPasswordActionState>>();

vi.mock("@/lib/actions/reset-password", () => ({
  resetPasswordAction: () => actionMock(),
}));

import { ResetPassword } from "./reset-password";

const UID = "6f9619ff-8b86-d011-b42d-00c04fc964ff";
const TOKEN = "Q2ZESjhL-nP_ab12CD"; // gitleaks:allow
const SUBMIT = "Spara nytt lösenord";

async function submit(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText("Nytt lösenord", { exact: true }), "ettNyttLosen123");
  await user.click(screen.getByRole("button", { name: SUBMIT }));
}

describe("ResetPassword", () => {
  beforeEach(() => vi.clearAllMocks());

  it("does not POST on mount", async () => {
    render(<ResetPassword uid={UID} token={TOKEN} />);

    expect(await screen.findByRole("button", { name: SUBMIT })).toBeInTheDocument();
    expect(actionMock).not.toHaveBeenCalled();
  });

  it("replaces the form with a focused success state and a sign-in link", async () => {
    actionMock.mockResolvedValue({ done: true });
    const user = userEvent.setup();
    render(<ResetPassword uid={UID} token={TOKEN} />);

    await submit(user);

    const heading = await screen.findByRole("heading", { name: "Lösenordet är ändrat" });
    await waitFor(() => expect(heading).toHaveFocus());
    expect(screen.getByRole("link", { name: "Logga in" })).toHaveAttribute("href", "/logga-in");
    expect(screen.queryByRole("button", { name: SUBMIT })).not.toBeInTheDocument();
  });

  it("REPLACES the form on a dead link and offers a way to request a new one", async () => {
    // The dead end this test exists to prevent: the copy says "request a new reset link" while the page
    // offers no affordance to do it, and a live submit button invites a retry no password can satisfy.
    actionMock.mockResolvedValue({
      error: "Länken är ogiltig eller har gått ut.",
      linkDead: true,
    });
    const user = userEvent.setup();
    render(<ResetPassword uid={UID} token={TOKEN} />);

    await submit(user);

    const heading = await screen.findByRole("heading", { name: "Länken går inte att använda" });
    await waitFor(() => expect(heading.parentElement).toHaveFocus());
    expect(screen.getByRole("link", { name: "Begär en ny återställningslänk" }))
      .toHaveAttribute("href", "/glomt-losenord");
    expect(screen.queryByRole("button", { name: SUBMIT })).not.toBeInTheDocument();
    // Status channel, not the error channel: this is not the user's mistake and not retryable here.
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("KEEPS the form mounted when the password is rejected, because the link still works", async () => {
    // The invariant that costs the most if it regresses: Identity verifies the token first, so a
    // breached-password rejection does not rotate the stamp and the SAME link is still usable. If this
    // component replaced the form on error, a user whose only fault was a weak password would be sent
    // to request a new link they do not need.
    // No linkDead — a rejected password leaves the token intact, so the same link still works.
    actionMock.mockResolvedValue({ error: "Lösenordet finns i kända dataintrång." });
    const user = userEvent.setup();
    render(<ResetPassword uid={UID} token={TOKEN} />);

    await submit(user);

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Lösenordet finns i kända dataintrång.");
    expect(screen.getByRole("button", { name: SUBMIT })).toBeInTheDocument();
    const field = screen.getByLabelText("Nytt lösenord", { exact: true });
    expect(field).toBeInTheDocument();
    // The error is wired to the field, not merely announced beside it: focus moves INTO the input, so
    // without aria-describedby a screen-reader user hears the label and hint and never the reason.
    expect(field).toHaveAttribute("aria-invalid", "true");
    expect(field.getAttribute("aria-describedby")).toContain(alert.id);
  });

  it("carries uid and token as hidden fields rather than re-reading the URL", async () => {
    const { container } = render(<ResetPassword uid={UID} token={TOKEN} />);

    expect(container.querySelector('input[name="uid"]')).toHaveValue(UID);
    expect(container.querySelector('input[name="token"]')).toHaveValue(TOKEN);
  });
});
