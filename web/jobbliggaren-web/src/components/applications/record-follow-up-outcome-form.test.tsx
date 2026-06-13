import type React from "react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RecordFollowUpOutcomeForm } from "./record-follow-up-outcome-form";
import type { ActionResult } from "@/lib/actions/applications";

const recordFollowUpOutcomeActionMock: ReturnType<
  typeof vi.fn<(formData: FormData) => Promise<ActionResult>>
> = vi.fn();

vi.mock("@/lib/actions/applications", () => ({
  recordFollowUpOutcomeAction: (
    _applicationId: string,
    _followUpId: string,
    formData: FormData
  ) => recordFollowUpOutcomeActionMock(formData),
}));

// Radix Select kräver pointer-capture/scrollIntoView-polyfills som inte finns
// i delad test-setup. Mocka som native <select> så formvärdet propagerar
// deterministiskt och a11y-props (id/aria-*) bevaras för assertions.
// onValueChange bryggas via en modul-scope-ref för att slippa React-context
// (vi.mock-factory hoistas över top-level-imports).
let selectOnValueChange: (v: string) => void = () => {};

vi.mock("@/components/ui/select", () => ({
  Select: ({
    children,
    value,
    onValueChange,
    name,
  }: {
    children: React.ReactNode;
    value: string;
    onValueChange: (v: string) => void;
    name?: string;
  }) => {
    selectOnValueChange = onValueChange;
    return (
      <>
        <input type="hidden" name={name} value={value} readOnly />
        {children}
      </>
    );
  },
  SelectTrigger: ({
    id,
    "aria-invalid": ariaInvalid,
    "aria-describedby": ariaDescribedBy,
  }: {
    id?: string;
    "aria-invalid"?: boolean;
    "aria-describedby"?: string;
  }) => (
    <select
      id={id}
      aria-invalid={ariaInvalid}
      aria-describedby={ariaDescribedBy}
      onChange={(e: React.ChangeEvent<HTMLSelectElement>) =>
        selectOnValueChange(e.target.value)
      }
    >
      <option value="">Välj utfall</option>
      <option value="Responded">Svar mottaget</option>
      <option value="NoResponse">Inget svar</option>
    </select>
  ),
  SelectContent: () => null,
  SelectItem: () => null,
  SelectValue: () => null,
}));

const baseProps = { applicationId: "app-1", followUpId: "fu-9" };

describe("RecordFollowUpOutcomeForm", () => {
  beforeEach(() => {
    recordFollowUpOutcomeActionMock.mockReset();
    recordFollowUpOutcomeActionMock.mockResolvedValue({ success: true });
  });

  it("renders the irreversibility notice paragraph linked via aria-describedby", () => {
    render(<RecordFollowUpOutcomeForm {...baseProps} />);

    const notice = document.getElementById("outcome-notice-fu-9");
    expect(notice).not.toBeNull();
    expect(notice).toHaveTextContent(
      "Utfallet kan inte ändras när det har sparats"
    );

    const trigger = screen.getByLabelText("Utfall");
    expect(trigger).toHaveAttribute("aria-describedby", "outcome-notice-fu-9");
  });

  it("disables 'Spara utfall' until an outcome is selected", async () => {
    const user = userEvent.setup();
    render(<RecordFollowUpOutcomeForm {...baseProps} />);

    expect(
      screen.getByRole("button", { name: "Spara utfall" })
    ).toBeDisabled();

    await user.selectOptions(screen.getByLabelText("Utfall"), "Responded");

    expect(
      screen.getByRole("button", { name: "Spara utfall" })
    ).not.toBeDisabled();
  });

  it("enters confirm stage on 'Spara utfall' click without submitting", async () => {
    const user = userEvent.setup();
    render(<RecordFollowUpOutcomeForm {...baseProps} />);

    await user.selectOptions(screen.getByLabelText("Utfall"), "Responded");
    await user.click(screen.getByRole("button", { name: "Spara utfall" }));

    expect(
      screen.getByText(/Spara utfallet/)
    ).toHaveTextContent(
      "Spara utfallet Svar mottaget? Detta går inte att ändra efteråt."
    );
    expect(recordFollowUpOutcomeActionMock).not.toHaveBeenCalled();
  });

  it("submits via recordFollowUpOutcomeAction when confirming with 'Spara <label>'", async () => {
    const user = userEvent.setup();
    render(<RecordFollowUpOutcomeForm {...baseProps} />);

    await user.selectOptions(screen.getByLabelText("Utfall"), "NoResponse");
    await user.click(screen.getByRole("button", { name: "Spara utfall" }));
    await user.click(
      screen.getByRole("button", { name: "Spara Inget svar" })
    );

    await waitFor(() => {
      expect(recordFollowUpOutcomeActionMock).toHaveBeenCalledTimes(1);
    });
    const firstCall = recordFollowUpOutcomeActionMock.mock.calls[0];
    expect(firstCall).toBeDefined();
    expect(firstCall![0].get("outcome")).toBe("NoResponse");
  });

  it("'Avbryt' in confirm stage returns to step 1 without submitting", async () => {
    const user = userEvent.setup();
    render(<RecordFollowUpOutcomeForm {...baseProps} />);

    await user.selectOptions(screen.getByLabelText("Utfall"), "Responded");
    await user.click(screen.getByRole("button", { name: "Spara utfall" }));
    await user.click(screen.getByRole("button", { name: "Avbryt" }));

    expect(
      screen.getByRole("button", { name: "Spara utfall" })
    ).toBeInTheDocument();
    expect(screen.queryByText(/Spara utfallet/)).not.toBeInTheDocument();
    expect(recordFollowUpOutcomeActionMock).not.toHaveBeenCalled();
  });

  it("changing outcome in confirm stage resets confirming back to step 1", async () => {
    const user = userEvent.setup();
    render(<RecordFollowUpOutcomeForm {...baseProps} />);

    await user.selectOptions(screen.getByLabelText("Utfall"), "Responded");
    await user.click(screen.getByRole("button", { name: "Spara utfall" }));
    expect(screen.getByText(/Spara utfallet/)).toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText("Utfall"), "NoResponse");

    expect(screen.queryByText(/Spara utfallet/)).not.toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Spara utfall" })
    ).toBeInTheDocument();
  });

  it("shows role=alert error and extends aria-describedby with errorId on failure", async () => {
    recordFollowUpOutcomeActionMock.mockResolvedValueOnce({
      success: false,
      error: "Utfallet är redan satt.",
    });
    const user = userEvent.setup();
    render(<RecordFollowUpOutcomeForm {...baseProps} />);

    await user.selectOptions(screen.getByLabelText("Utfall"), "Responded");
    await user.click(screen.getByRole("button", { name: "Spara utfall" }));
    await user.click(
      screen.getByRole("button", { name: "Spara Svar mottaget" })
    );

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveAttribute("id", "outcome-error-fu-9");
    expect(alert).toHaveTextContent("Utfallet är redan satt.");

    const trigger = screen.getByLabelText("Utfall");
    expect(trigger).toHaveAttribute(
      "aria-describedby",
      "outcome-notice-fu-9 outcome-error-fu-9"
    );
  });
});
