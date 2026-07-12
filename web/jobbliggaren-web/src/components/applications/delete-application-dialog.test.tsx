import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { DeleteApplicationDialog } from "./delete-application-dialog";
import type { ActionResult } from "@/lib/actions/applications";

const deleteApplicationActionMock =
  vi.fn<(id: string) => Promise<ActionResult>>();

vi.mock("@/lib/actions/applications", () => ({
  deleteApplicationAction: (id: string) => deleteApplicationActionMock(id),
}));

describe("DeleteApplicationDialog (#782 — destruktiv hard-delete-bekräftelse)", () => {
  beforeEach(() => {
    deleteApplicationActionMock.mockReset();
    deleteApplicationActionMock.mockResolvedValue({ success: true });
  });

  it("visar konsekvenstext FÖRE handling; specifik knapp, aldrig 'Bekräfta'/'OK'", () => {
    render(
      <DeleteApplicationDialog
        open
        onOpenChange={vi.fn()}
        applicationId="app-1"
      />
    );

    expect(
      screen.getByRole("dialog", { name: "Radera ansökan?" })
    ).toBeInTheDocument();
    expect(
      screen.getByText(/tas bort permanent\. Det går inte att ångra/)
    ).toBeInTheDocument();
    // Den specifika destruktiva verben — inte "Bekräfta"/"OK".
    expect(
      screen.getByRole("button", { name: "Radera ansökan" })
    ).toBeInTheDocument();
    // Ingen radering ännu (dialogen är förhandsvarningen).
    expect(deleteApplicationActionMock).not.toHaveBeenCalled();
  });

  it("bekräftelse anropar deleteApplicationAction och signalerar borttagning", async () => {
    const user = userEvent.setup();
    const onOpenChange = vi.fn();
    const onDeleted = vi.fn();
    render(
      <DeleteApplicationDialog
        open
        onOpenChange={onOpenChange}
        applicationId="app-1"
        onDeleted={onDeleted}
      />
    );

    await user.click(screen.getByRole("button", { name: "Radera ansökan" }));

    await waitFor(() =>
      expect(deleteApplicationActionMock).toHaveBeenCalledWith("app-1")
    );
    expect(onOpenChange).toHaveBeenCalledWith(false);
    expect(onDeleted).toHaveBeenCalled();
  });

  it("Avbryt stänger dialogen utan att radera", async () => {
    const user = userEvent.setup();
    const onOpenChange = vi.fn();
    render(
      <DeleteApplicationDialog
        open
        onOpenChange={onOpenChange}
        applicationId="app-1"
      />
    );

    await user.click(screen.getByRole("button", { name: "Avbryt" }));

    expect(onOpenChange).toHaveBeenCalledWith(false);
    expect(deleteApplicationActionMock).not.toHaveBeenCalled();
  });

  it("visar serverfel i dialogen vid misslyckad radering", async () => {
    deleteApplicationActionMock.mockResolvedValue({
      success: false,
      error: "Det gick inte att ta bort ansökan.",
    });
    const user = userEvent.setup();
    const onDeleted = vi.fn();
    render(
      <DeleteApplicationDialog
        open
        onOpenChange={vi.fn()}
        applicationId="app-1"
        onDeleted={onDeleted}
      />
    );

    await user.click(screen.getByRole("button", { name: "Radera ansökan" }));

    expect(
      await screen.findByRole("alert")
    ).toHaveTextContent("Det gick inte att ta bort ansökan.");
    // Ett misslyckande signalerar ALDRIG borttagning (ingen navigering bort).
    expect(onDeleted).not.toHaveBeenCalled();
  });
});
