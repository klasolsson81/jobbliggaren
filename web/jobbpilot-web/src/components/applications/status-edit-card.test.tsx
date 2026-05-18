import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { StatusEditCard } from "./status-edit-card";
import type { ActionResult } from "@/lib/actions/applications";
import type { ApplicationStatus } from "@/lib/types/applications";

// Server-action mockas per repo-mönster (jfr delete-account-dialog.test /
// record-follow-up-outcome-form.test) — vi.mock-factory hoistas över
// top-level-imports, så vi bryggar via en modul-scope-ref-fn.
const transitionStatusActionMock =
  vi.fn<
    (
      applicationId: string,
      targetStatus: ApplicationStatus
    ) => Promise<ActionResult>
  >();

vi.mock("@/lib/actions/applications", () => ({
  transitionStatusAction: (
    applicationId: string,
    targetStatus: ApplicationStatus
  ) => transitionStatusActionMock(applicationId, targetStatus),
}));

// OBS: ingen Radix-mock. Status-card.test.tsx (ersatt fil) körde äkta Radix
// Dialog grönt i samma jsdom-setup, och Radix RadioGroup kräver inte
// pointer-capture/scrollIntoView för klick-baserad selektion (till skillnad
// mot Radix Select i record-follow-up-outcome-form.test.tsx). Vi kör därför
// äkta RadioGroup + Dialog och driver allt via klick (deterministiskt,
// ej pil-navigering som behöver roving-fokus i jsdom).

const baseProps = { applicationId: "app-1" };

describe("StatusEditCard", () => {
  beforeEach(() => {
    transitionStatusActionMock.mockReset();
    transitionStatusActionMock.mockResolvedValue({ success: true });
  });

  it("renders current status as a single StatusPill (no locked self-radio)", () => {
    render(<StatusEditCard {...baseProps} currentStatus="Submitted" />);

    // "Skickad" = svensk etikett för Submitted. Nuvarande status visas EN
    // gång som StatusPill bredvid "Nuvarande status:"-labeln (instruktions-
    // raden upprepar etiketten i löptext, men det är inte en andra pill).
    // Avgörande invariant: ingen radio-knapp med nuvarande status (Submitted
    // är inte i sin egen ALLOWED_TRANSITIONS-lista — ingen låst self-radio).
    const label = screen.getByText("Nuvarande status:");
    const pillRow = label.parentElement as HTMLElement;
    expect(within(pillRow).getByText("Skickad")).toBeInTheDocument();
    expect(
      screen.queryByRole("radio", { name: "Skickad" })
    ).not.toBeInTheDocument();
  });

  it("renders the visible instruction line and links the radiogroup via aria-labelledby (L1)", () => {
    render(<StatusEditCard {...baseProps} currentStatus="Submitted" />);

    const instruction = screen.getByText(/Välj ny status\. Nuvarande status är/);
    expect(instruction).toBeVisible();
    const instructionId = instruction.getAttribute("id");
    expect(instructionId).toBeTruthy();

    const group = screen.getByRole("radiogroup");
    expect(group).toHaveAttribute("aria-labelledby", instructionId);
  });

  it("renders ONLY allowed transitions as radios (real ALLOWED_TRANSITIONS)", () => {
    render(<StatusEditCard {...baseProps} currentStatus="Submitted" />);

    // Submitted -> Acknowledged (Bekräftad), Rejected (Nekad), Withdrawn (Återtagen)
    expect(
      screen.getByRole("radio", { name: "Bekräftad" })
    ).toBeInTheDocument();
    expect(screen.getByRole("radio", { name: "Nekad" })).toBeInTheDocument();
    expect(
      screen.getByRole("radio", { name: "Återtagen" })
    ).toBeInTheDocument();
    // Ej tillåtna mål renderas inte
    expect(
      screen.queryByRole("radio", { name: "Intervju bokad" })
    ).not.toBeInTheDocument();
    expect(screen.getAllByRole("radio")).toHaveLength(3);
  });

  it("keeps [Spara] disabled until a status differing from current is selected (Variant A)", async () => {
    const user = userEvent.setup();
    render(<StatusEditCard {...baseProps} currentStatus="Submitted" />);

    const save = screen.getByRole("button", { name: "Spara" });
    expect(save).toBeDisabled();

    await user.click(screen.getByRole("radio", { name: "Bekräftad" }));

    await waitFor(() => {
      expect(screen.getByRole("button", { name: "Spara" })).not.toBeDisabled();
    });
  });

  it("calls transitionStatusAction with the selected target for a non-destructive transition", async () => {
    const user = userEvent.setup();
    render(<StatusEditCard {...baseProps} currentStatus="Submitted" />);

    await user.click(screen.getByRole("radio", { name: "Bekräftad" }));
    await user.click(screen.getByRole("button", { name: "Spara" }));

    await waitFor(() => {
      expect(transitionStatusActionMock).toHaveBeenCalledTimes(1);
    });
    expect(transitionStatusActionMock).toHaveBeenCalledWith(
      "app-1",
      "Acknowledged"
    );
    // Ingen bekräftelsedialog för icke-destruktiv övergång
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("renders a single primary button (no radiogroup) when exactly one transition exists", () => {
    // Draft -> [Submitted] (en övergång)
    render(<StatusEditCard {...baseProps} currentStatus="Draft" />);

    expect(screen.queryByRole("radiogroup")).not.toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Markera som Skickad" })
    ).toBeInTheDocument();
  });

  it("calls action directly from the single-transition button", async () => {
    const user = userEvent.setup();
    render(<StatusEditCard {...baseProps} currentStatus="Draft" />);

    await user.click(
      screen.getByRole("button", { name: "Markera som Skickad" })
    );

    await waitFor(() => {
      expect(transitionStatusActionMock).toHaveBeenCalledTimes(1);
    });
    expect(transitionStatusActionMock).toHaveBeenCalledWith(
      "app-1",
      "Submitted"
    );
  });

  it("shows civic text and no radiogroup/Spara when there are zero transitions", () => {
    // Accepted har tom övergångslista
    render(<StatusEditCard {...baseProps} currentStatus="Accepted" />);

    expect(
      screen.getByText("Den här ansökan är avslutad och kan inte ändras.")
    ).toBeInTheDocument();
    expect(screen.queryByRole("radiogroup")).not.toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Spara" })
    ).not.toBeInTheDocument();
    // Nuvarande status fortfarande som pill
    expect(screen.getAllByText("Accepterad")).toHaveLength(1);
  });

  it("opens a confirmation Dialog for a destructive transition without calling the action (L2)", async () => {
    const user = userEvent.setup();
    render(<StatusEditCard {...baseProps} currentStatus="Submitted" />);

    await user.click(screen.getByRole("radio", { name: "Nekad" }));
    await user.click(screen.getByRole("button", { name: "Spara" }));

    const dialog = await screen.findByRole("dialog");
    expect(
      within(dialog).getByRole("heading", { name: "Markera som Nekad?" })
    ).toBeInTheDocument();
    expect(dialog).toHaveTextContent("Det går inte att ångra");
    // Action EJ anropad förrän bekräftelse
    expect(transitionStatusActionMock).not.toHaveBeenCalled();
  });

  it("shows the additive inline consequence text when a destructive status is selected (L2)", async () => {
    const user = userEvent.setup();
    render(<StatusEditCard {...baseProps} currentStatus="Submitted" />);

    await user.click(screen.getByRole("radio", { name: "Återtagen" }));

    expect(
      screen.getByText(/avslutar ansökan\. Det går inte att ångra/)
    ).toBeInTheDocument();
  });

  it("calls the action only after confirming the destructive transition in the Dialog", async () => {
    const user = userEvent.setup();
    render(<StatusEditCard {...baseProps} currentStatus="Submitted" />);

    await user.click(screen.getByRole("radio", { name: "Nekad" }));
    await user.click(screen.getByRole("button", { name: "Spara" }));
    const dialog = await screen.findByRole("dialog");
    await user.click(
      within(dialog).getByRole("button", { name: "Markera som Nekad" })
    );

    await waitFor(() => {
      expect(transitionStatusActionMock).toHaveBeenCalledTimes(1);
    });
    expect(transitionStatusActionMock).toHaveBeenCalledWith("app-1", "Rejected");
  });

  it("closes the Dialog without calling the action when 'Avbryt' is clicked", async () => {
    const user = userEvent.setup();
    render(<StatusEditCard {...baseProps} currentStatus="Submitted" />);

    await user.click(screen.getByRole("radio", { name: "Återtagen" }));
    await user.click(screen.getByRole("button", { name: "Spara" }));
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: "Avbryt" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });
    expect(transitionStatusActionMock).not.toHaveBeenCalled();
  });

  it("shows role=alert with error text when the action fails", async () => {
    transitionStatusActionMock.mockResolvedValueOnce({
      success: false,
      error: "Övergången är inte tillåten.",
    });
    const user = userEvent.setup();
    render(<StatusEditCard {...baseProps} currentStatus="Submitted" />);

    await user.click(screen.getByRole("radio", { name: "Bekräftad" }));
    await user.click(screen.getByRole("button", { name: "Spara" }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Övergången är inte tillåten.");
  });
});
