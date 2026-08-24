import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AddNoteForm } from "./add-note-form";
import type { AddNoteActionState } from "@/lib/actions/applications";

const addNoteActionMock = vi.fn<(formData: FormData) => Promise<AddNoteActionState>>();

vi.mock("@/lib/actions/applications", () => ({
  addNoteAction: (_applicationId: string, formData: FormData) =>
    addNoteActionMock(formData),
}));

const SUBMIT = "Spara notering";

describe("AddNoteForm", () => {
  beforeEach(() => {
    addNoteActionMock.mockReset();
    addNoteActionMock.mockResolvedValue({ success: true });
  });

  // React 19 resets this uncontrolled form after EVERY action, so a failed save destroyed the whole
  // note — which can run to several paragraphs. The action echoes the submitted text back.
  it("re-seeds the note from the action's echo rather than losing it to the reset", async () => {
    // The echo deliberately differs from what was typed. In production they are equal, and that is
    // exactly why the difference is needed here: an assertion on the typed string alone would also
    // pass if the field were simply never reset. Same reason LoginForm's #791 test types one
    // address and echoes another.
    addNoteActionMock.mockResolvedValue({
      success: false,
      error: "Det gick inte att spara noteringen.",
      values: { content: "ekot fran servern" },
    });

    const user = userEvent.setup();
    render(<AddNoteForm applicationId="app-1" />);

    await user.type(screen.getByLabelText("Notering"), "Ringde rekryteraren");
    await user.click(screen.getByRole("button", { name: SUBMIT }));

    await screen.findByRole("alert");
    await waitFor(() => {
      expect(screen.getByLabelText("Notering")).toHaveValue("ekot fran servern");
    });
  });

  it("announces the failure in a live region and moves focus to it", async () => {
    // The message was a plain <p> with no role: nothing announced it, and with the submit button
    // disabled during the action focus fell to <body>, so the next Tab restarted at the top of the
    // page. This form names no field, so the message is the only honest focus target.
    addNoteActionMock.mockResolvedValue({
      success: false,
      error: "Det gick inte att spara noteringen.",
      values: { content: "Ringde rekryteraren" },
    });

    const user = userEvent.setup();
    render(<AddNoteForm applicationId="app-1" />);

    await user.type(screen.getByLabelText("Notering"), "Ringde rekryteraren");
    await user.click(screen.getByRole("button", { name: SUBMIT }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Det gick inte att spara noteringen.");
    await waitFor(() => expect(alert).toHaveFocus());
    // Programmatic focus only — the message must not join the Tab order.
    expect(alert).toHaveAttribute("tabindex", "-1");
  });

  it("clears the field on a successful save, so the next note starts blank", async () => {
    // The counterfactual for the re-seed: it is a property of FAILURE. A success collapses the
    // disclosure and must leave nothing behind for the next note to inherit.
    const onSuccess = vi.fn();
    const user = userEvent.setup();
    render(<AddNoteForm applicationId="app-1" onSuccess={onSuccess} />);

    await user.type(screen.getByLabelText("Notering"), "Ringde rekryteraren");
    await user.click(screen.getByRole("button", { name: SUBMIT }));

    await waitFor(() => expect(onSuccess).toHaveBeenCalled());
    expect(screen.getByLabelText("Notering")).toHaveValue("");
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });
});
