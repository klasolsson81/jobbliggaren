import { Component, type ReactNode } from "react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, fireEvent } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { NewApplicationForm } from "./new-application-form";
import type { ActionResult } from "@/lib/actions/_action-result";

const createApplicationActionMock =
  vi.fn<(formData: FormData) => Promise<ActionResult>>();

vi.mock("@/lib/actions/applications", () => ({
  createApplicationAction: (
    _prevState: ActionResult | null,
    formData: FormData
  ) => createApplicationActionMock(formData),
}));

const SUBMIT = "Skapa ansökan";

// Stands in for the RedirectBoundary Next mounts above every route: it is what receives the
// redirect signal in production. Without it the rejection would surface as an unhandled test error
// rather than as the navigation it is.
let caught: unknown = null;

class CatchBoundary extends Component<{ children: ReactNode }> {
  state = { failed: false };

  static getDerivedStateFromError() {
    return { failed: true };
  }

  componentDidCatch(error: unknown) {
    caught = error;
  }

  render() {
    return this.state.failed ? null : this.props.children;
  }
}

// Labels carry a visually hidden required marker, so the two mandatory fields are matched by
// prefix. The other three have plain labels.
const field = {
  title: () => screen.getByLabelText(/^Jobbtitel/),
  company: () => screen.getByLabelText(/^Företag/),
  url: () => screen.getByLabelText("Annonslänk"),
  expiresAt: () => screen.getByLabelText("Sista ansökningsdag"),
  coverLetter: () => screen.getByLabelText("Personligt brev"),
};

// `<input type="date">` has no jsdom UI, and typing it a character at a time produces invalid
// intermediate values the element rejects, so it is set in one write. React's change plugin picks
// this up exactly as it would a real edit.
function setDate(value: string) {
  fireEvent.change(field.expiresAt(), { target: { value } });
}

const TYPED = {
  title: "Backend-utvecklare",
  company: "Volvo",
  url: "https://example.com/jobb/1",
  expiresAt: "2026-09-01",
  coverLetter: "Jag soker tjansten for att jag vill bygga betalsystem.",
};

async function fillEveryField(user: ReturnType<typeof userEvent.setup>) {
  await user.type(field.title(), TYPED.title);
  await user.type(field.company(), TYPED.company);
  await user.type(field.url(), TYPED.url);
  setDate(TYPED.expiresAt);
  await user.type(field.coverLetter(), TYPED.coverLetter);
}

describe("NewApplicationForm", () => {
  beforeEach(() => {
    caught = null;
    createApplicationActionMock.mockReset();
    createApplicationActionMock.mockResolvedValue({
      success: false,
      error: "Kunde inte spara ansökan.",
    });
  });

  // As an uncontrolled `<form action={formAction}>` a failed save took all five fields with it:
  // React 19 resets such a form after every action, and this one had no `defaultValue` to fall
  // back on (error-surface matrix rank 1, RP-26). React Hook Form owns the values now and the form
  // has no form action, so there is no reset to survive.
  it("keeps all five typed fields after a failed save the action returns nothing back from", async () => {
    // The failure carries an error string and nothing else — `createApplicationAction` hands back
    // no submitted values at all. That is what makes this measure the actual mechanism: survival is
    // React Hook Form's ownership of the values, not a server echo re-seeding the inputs.
    const user = userEvent.setup();
    render(<NewApplicationForm />);

    await fillEveryField(user);
    await user.click(screen.getByRole("button", { name: SUBMIT }));

    await screen.findByRole("alert");

    expect(field.title()).toHaveValue(TYPED.title);
    expect(field.company()).toHaveValue(TYPED.company);
    expect(field.url()).toHaveValue(TYPED.url);
    expect(field.expiresAt()).toHaveValue(TYPED.expiresAt);
    expect(field.coverLetter()).toHaveValue(TYPED.coverLetter);
  });

  it("posts every typed value, and resends all of them on a retry", async () => {
    const user = userEvent.setup();
    render(<NewApplicationForm />);

    await fillEveryField(user);
    await user.click(screen.getByRole("button", { name: SUBMIT }));
    await screen.findByRole("alert");

    const first = createApplicationActionMock.mock.calls[0]?.[0];
    if (!first) throw new Error("createApplicationAction was not invoked");
    expect(first.get("title")).toBe(TYPED.title);
    expect(first.get("company")).toBe(TYPED.company);
    expect(first.get("url")).toBe(TYPED.url);
    expect(first.get("expiresAt")).toBe(TYPED.expiresAt);
    expect(first.get("coverLetter")).toBe(TYPED.coverLetter);

    // The retry has to carry the same payload rather than the empty strings a reset would leave.
    // The action returns a failure again — the shape it genuinely produces; a SUCCESS is a redirect
    // rejection, measured in its own test below.
    await user.click(screen.getByRole("button", { name: SUBMIT }));
    await waitFor(() =>
      expect(createApplicationActionMock).toHaveBeenCalledTimes(2)
    );
    const retry = createApplicationActionMock.mock.calls[1]?.[0];
    if (!retry) throw new Error("createApplicationAction was not invoked twice");
    for (const key of Object.keys(TYPED) as (keyof typeof TYPED)[]) {
      expect(retry.get(key)).toBe(TYPED[key]);
    }
  });

  it("announces a nameless failure in a live region and moves focus to it", async () => {
    // The matrix measured focus falling to <body> here: the submit button is disabled while the
    // action runs, and a server failure names no field, so there was nowhere for the caret to go.
    const user = userEvent.setup();
    render(<NewApplicationForm />);

    await fillEveryField(user);
    await user.click(screen.getByRole("button", { name: SUBMIT }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Kunde inte spara ansökan.");
    await waitFor(() => expect(alert).toHaveFocus());
    // Programmatic focus only — the message must not join the Tab order.
    expect(alert).toHaveAttribute("tabindex", "-1");

    // A failure that belongs to no field leaves every input unmarked.
    expect(field.title()).not.toHaveAttribute("aria-invalid");
    expect(field.url()).not.toHaveAttribute("aria-invalid");
  });

  it("refuses a non-http link in Swedish, and marks and focuses the link field", async () => {
    // The client mirror of `makeCreateApplicationSchema` — the same builder the action runs, so the
    // message is the server's, not a second copy of it.
    //
    // The scheme rule is chosen deliberately: `ftp://example.com/jobb` is a well-formed absolute
    // URL, so the input's native `type="url"` gate passes it and the zod arm is the one the user
    // actually reaches in a browser. (Contrast the two `required` fields, whose native bubble fires
    // first by design.)
    const user = userEvent.setup();
    render(<NewApplicationForm />);

    await user.type(field.title(), TYPED.title);
    await user.type(field.company(), TYPED.company);
    await user.type(field.url(), "ftp://example.com/jobb");
    await user.click(screen.getByRole("button", { name: SUBMIT }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent(
      "Annonslänken måste börja med http:// eller https://."
    );
    expect(createApplicationActionMock).not.toHaveBeenCalled();

    // The refusal names one control, so it marks and focuses that control, not the message row.
    const url = field.url();
    expect(url).toHaveAttribute("aria-invalid", "true");
    // The hint states the very constraint the refusal is about, so it is kept alongside the message
    // rather than replaced by it.
    expect(url.getAttribute("aria-describedby")).toBe(`url-hint ${alert.id}`);
    await waitFor(() => expect(url).toHaveFocus());

    // No other field is implicated, and nothing the user typed is lost.
    expect(field.title()).not.toHaveAttribute("aria-invalid");
    expect(field.title()).toHaveValue(TYPED.title);
    expect(field.company()).toHaveValue(TYPED.company);
  });

  it("does not render a success as an error when the action redirects", async () => {
    // What production produces on success is NOT `{ success: true }` — the action ends in
    // `redirect()`, and Next rejects the action promise with that redirect so its router can
    // navigate (`server-action-reducer`, Next 16.3.0, measured 2026-08-25). So the success premise
    // here is a rejection, and the guarantee under test is that the form treats it as the success
    // signal it is: no error message, and the redirect left to propagate to the boundary Next
    // provides in production.
    class RedirectError extends Error {}
    createApplicationActionMock.mockRejectedValue(
      new RedirectError("NEXT_REDIRECT")
    );

    const user = userEvent.setup();
    render(
      <CatchBoundary>
        <NewApplicationForm />
      </CatchBoundary>
    );

    await fillEveryField(user);
    await user.click(screen.getByRole("button", { name: SUBMIT }));

    await waitFor(() =>
      expect(createApplicationActionMock).toHaveBeenCalledTimes(1)
    );
    await waitFor(() => expect(caught).toBeInstanceOf(RedirectError));
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });
});
