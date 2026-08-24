import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ForgotPasswordActionState } from "@/lib/actions/forgot-password";

// #1171 — the three outcome channels of the forgot-password form, and the counterfactual that keeps
// them apart. The refusal must render in the STATUS channel with the submit affordance GONE, while an
// ordinary failure keeps the form alive — a live red button on a request no retry can satisfy reads as
// "you typed something wrong". Assertions are literal Swedish because the test renderer wraps every
// render in NextIntlClientProvider with the sv catalogue.

const actionMock = vi.fn<() => Promise<ForgotPasswordActionState>>();

vi.mock("@/lib/actions/forgot-password", () => ({
  requestPasswordResetAction: () => actionMock(),
}));

import { ForgotPasswordForm } from "./ForgotPasswordForm";

const SUBMIT = "Skicka återställningslänk";

async function submit(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText("E-postadress"), "nagon@exempel.se");
  await user.click(screen.getByRole("button", { name: SUBMIT }));
}

describe("ForgotPasswordForm", () => {
  beforeEach(() => vi.clearAllMocks());

  it("replaces the form with a status panel whose copy does not confirm the account exists", async () => {
    // The backend answers an identical 202 for a known and an unknown address. If this panel said
    // "we sent a link to your address" the UI would confirm existence after the API refused to — the
    // enumeration oracle rebuilt in the frontend.
    actionMock.mockResolvedValue({ success: true });
    const user = userEvent.setup();
    render(<ForgotPasswordForm />);

    await submit(user);

    const heading = await screen.findByRole("heading", { name: "Kontrollera din inkorg" });
    expect(heading).toBeInTheDocument();
    // The instruction belongs to the field and must not survive it — left on the page it tells the
    // user to fill in something that is gone.
    expect(screen.queryByText(/Skriv in din e-postadress/)).not.toBeInTheDocument();
    expect(screen.getByText(/Om adressen hör till ett konto/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: SUBMIT })).not.toBeInTheDocument();
  });

  it("renders a refusal in the status channel and removes the submit affordance", async () => {
    actionMock.mockResolvedValue({
      success: false,
      refused: true,
      error: "E-postutskick är inte aktiverat just nu.",
    });
    const user = userEvent.setup();
    render(<ForgotPasswordForm />);

    await submit(user);

    const panel = await screen.findByText("E-postutskick är inte aktiverat just nu.");
    expect(panel.parentElement).toHaveAttribute("role", "status");
    // Not the error channel: role="alert" + a live button would invite a retry that cannot succeed.
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: SUBMIT })).not.toBeInTheDocument();
    await waitFor(() => expect(panel.parentElement).toHaveFocus());
    // A way out. The layout renders no login link (showLogin={false}), so without this the refused
    // page body offers no navigation at all.
    expect(screen.getByRole("link", { name: "Tillbaka till inloggningen" }))
      .toHaveAttribute("href", "/logga-in");
  });

  it("keeps the retry affordance alive when an ORDINARY failure is shown", async () => {
    // THE COUNTERFACTUAL, in its own test so a regression names which property broke. Without it the
    // test above would still pass if the component removed the form on every failure.
    actionMock.mockResolvedValue({
      success: false,
      error: "Det gick inte att skicka någon återställningslänk just nu.",
    });
    const user = userEvent.setup();
    render(<ForgotPasswordForm />);

    await submit(user);

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Det gick inte att skicka");
    expect(screen.getByRole("button", { name: SUBMIT })).toBeInTheDocument();
    const field = screen.getByLabelText("E-postadress");
    expect(field).toBeInTheDocument();
    expect(field).toHaveAttribute("aria-invalid", "true");
    expect(field.getAttribute("aria-describedby")).toContain(alert.id);
  });

  it("keeps the address in the field when an ordinary failure asks for a retry", async () => {
    // The failure copy invites a retry, and React 19 had just emptied the only field that retry
    // needs. The action echoes the address it actually sent (trimmed) and the form re-seeds it.
    //
    // The echo deliberately differs from what `submit` types. In production they are equal, and
    // that is why the difference is needed: asserting the typed string would also pass if the field
    // were never reset at all. LoginForm's #791 test types one address and echoes another for the
    // same reason.
    actionMock.mockResolvedValue({
      success: false,
      error: "Det gick inte att skicka någon återställningslänk just nu.",
      values: { email: "ekot@exempel.se" },
    });
    const user = userEvent.setup();
    render(<ForgotPasswordForm />);

    await submit(user);

    await screen.findByRole("alert");
    await waitFor(() => {
      expect(screen.getByLabelText("E-postadress")).toHaveValue("ekot@exempel.se");
    });
  });

  it("starts empty, so nothing carries over into a form that has not been submitted", async () => {
    // The seed is a property of a FAILED submit, not of the field. A first render must not put an
    // address in an input the visitor has not filled in.
    render(<ForgotPasswordForm />);
    expect(screen.getByLabelText("E-postadress")).toHaveValue("");
  });

  it("offers a way back to sign-in before and after submitting", async () => {
    actionMock.mockResolvedValue({ success: true });
    const user = userEvent.setup();
    render(<ForgotPasswordForm />);

    expect(
      screen.getByRole("link", { name: "Tillbaka till inloggningen" }),
    ).toHaveAttribute("href", "/logga-in");

    await submit(user);

    expect(
      await screen.findByRole("link", { name: "Tillbaka till inloggningen" }),
    ).toHaveAttribute("href", "/logga-in");
  });
});
