import type React from "react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AddFollowUpForm } from "./add-follow-up-form";
import type { ActionResult } from "@/lib/actions/_action-result";

const addFollowUpActionMock = vi.fn<(formData: FormData) => Promise<ActionResult>>();

vi.mock("@/lib/actions/applications", () => ({
  addFollowUpAction: (_applicationId: string, formData: FormData) =>
    addFollowUpActionMock(formData),
}));

// Radix Select needs pointer-capture/scrollIntoView polyfills the shared test setup does not carry,
// so it is mocked as a native <select> — the shape record-follow-up-outcome-form.test.tsx uses, and
// the same CONTROLLED contract the component now drives through RHF's Controller.
//
// What this seam still cannot exercise: the real trigger rendering the selected item's label. That
// is Radix's documented controlled API, already relied on in production by
// record-follow-up-outcome-form. It no longer hides anything about reset behaviour — the form has
// no form action, so nothing resets the DOM and there is no reset semantics left to reproduce.
//
// It also drops the NATIVE-REQUIRED dimension: real Radix renders a visually hidden <select> that
// carries `required`, this mock's <select> carries none. That difference is why the channel's
// `required` was removed at the usage site — browser probe 2026-08-24, PR #1512, measured the
// native bubble pre-empting the zod arm entirely — and it means no test in this file can measure
// whether native validation fires before the zod gate.
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
    "aria-required": ariaRequired,
  }: {
    id?: string;
    "aria-invalid"?: boolean;
    "aria-describedby"?: string;
    "aria-required"?: boolean | "true" | "false";
  }) => (
    <select
      id={id}
      aria-invalid={ariaInvalid}
      aria-describedby={ariaDescribedBy}
      aria-required={ariaRequired}
      onChange={(e: React.ChangeEvent<HTMLSelectElement>) =>
        selectOnValueChange(e.target.value)
      }
    >
      <option value="">Välj kanal</option>
      <option value="Email">E-post</option>
      <option value="Phone">Telefon</option>
    </select>
  ),
  SelectContent: () => null,
  SelectItem: () => null,
  SelectValue: () => null,
}));

const SUBMIT = "Lägg till uppföljning";
const NOTE = "Anteckning (valfritt)";

describe("AddFollowUpForm", () => {
  beforeEach(() => {
    addFollowUpActionMock.mockReset();
    addFollowUpActionMock.mockResolvedValue({ success: true });
  });

  // As an uncontrolled `<form action={serverAction}>` a failed save took all three fields with it:
  // React 19 resets such a form after every action (browser-measured, error-surface matrix RP-27).
  // React Hook Form owns the values now, and this form no longer has a form action, so there is no
  // reset to survive.
  it("keeps note, date and channel after a failed save the action returns nothing back from", async () => {
    // The failure carries the error and nothing else — `addFollowUpAction` hands back no submitted
    // values at all. That is what makes this measure the actual mechanism: survival is React Hook
    // Form's ownership of the values, not a server echo re-seeding the inputs.
    addFollowUpActionMock.mockResolvedValue({
      success: false,
      error: "Det gick inte att lägga till uppföljningen.",
    });

    const user = userEvent.setup();
    render(<AddFollowUpForm applicationId="app-1" />);

    const dateBeforeSubmit = (screen.getByLabelText("Datum") as HTMLInputElement).value;

    await user.selectOptions(screen.getByLabelText("Kanal"), "Email");
    await user.type(screen.getByLabelText(NOTE), "Ringer pa fredag");
    await user.click(screen.getByRole("button", { name: SUBMIT }));

    await screen.findByRole("alert");

    expect(screen.getByLabelText(NOTE)).toHaveValue("Ringer pa fredag");
    expect(screen.getByLabelText("Datum")).toHaveValue(dateBeforeSubmit);

    // The channel has no visible value through the mock, so it is measured where it matters: the
    // retry has to resend it rather than an empty string.
    addFollowUpActionMock.mockResolvedValue({ success: true });
    await user.click(screen.getByRole("button", { name: SUBMIT }));

    await waitFor(() => expect(addFollowUpActionMock).toHaveBeenCalledTimes(2));
    const retry = addFollowUpActionMock.mock.calls[1]?.[0];
    if (!retry) throw new Error("addFollowUpAction was not invoked a second time");
    expect(retry.get("channel")).toBe("Email");
    expect(retry.get("scheduledAt")).toBe(dateBeforeSubmit);
    expect(retry.get("note")).toBe("Ringer pa fredag");
  });

  it("announces the failure in a live region and moves focus to it", async () => {
    addFollowUpActionMock.mockResolvedValue({
      success: false,
      error: "Det gick inte att lägga till uppföljningen.",
    });

    const user = userEvent.setup();
    render(<AddFollowUpForm applicationId="app-1" />);

    await user.selectOptions(screen.getByLabelText("Kanal"), "Email");
    await user.click(screen.getByRole("button", { name: SUBMIT }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Det gick inte att lägga till uppföljningen.");
    await waitFor(() => expect(alert).toHaveFocus());
    // Programmatic focus only — the message must not join the Tab order.
    expect(alert).toHaveAttribute("tabindex", "-1");
  });

  it("refuses an unpicked channel in Swedish, and marks and focuses the trigger", async () => {
    // The client mirror of `makeAddFollowUpSchema` — the same builder the action runs, so the
    // message is the server's, not a second copy of it. The server stays authoritative: this only
    // saves the round trip. Reachable in a browser only because the channel Select carries no
    // native `required` (see the seam docblock above and the usage site).
    const user = userEvent.setup();
    render(<AddFollowUpForm applicationId="app-1" />);

    await user.click(screen.getByRole("button", { name: SUBMIT }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Välj en kanal.");
    expect(addFollowUpActionMock).not.toHaveBeenCalled();

    // The refusal names one control, so it marks and focuses that control — not the message row.
    const trigger = screen.getByLabelText("Kanal");
    expect(trigger).toHaveAttribute("aria-invalid", "true");
    expect(trigger.getAttribute("aria-describedby")).toBe(alert.id);
    await waitFor(() => expect(trigger).toHaveFocus());

    // Dropping native `required` (the unreachable-gate fix) must not silence the requiredness
    // channel entirely: the visible trigger carries it as aria-required (a11y skill §5).
    expect(trigger).toHaveAttribute("aria-required", "true");
  });

  it("clears the form on a successful save, and only then", async () => {
    // The counterfactual for the survival above: the values persist through FAILURE, and a success
    // is the one moment this form is deliberately emptied.
    const onSuccess = vi.fn();
    const user = userEvent.setup();
    render(<AddFollowUpForm applicationId="app-1" onSuccess={onSuccess} />);

    await user.selectOptions(screen.getByLabelText("Kanal"), "Email");
    await user.type(screen.getByLabelText(NOTE), "Ringer pa fredag");
    await user.click(screen.getByRole("button", { name: SUBMIT }));

    await waitFor(() => expect(onSuccess).toHaveBeenCalledTimes(1));
    expect(screen.getByLabelText(NOTE)).toHaveValue("");
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();

    // The date is re-read on clear rather than rewound to the mount time, so a second follow-up in
    // the same session does not open on a stale clock.
    expect(screen.getByLabelText("Datum")).not.toHaveValue("");
  });

  it("re-reads the clock when it clears, so a second follow-up does not open on the mount time", async () => {
    // The guarantee the docblock makes about `reset({ scheduledAt: localDatetimeNow() })`. Fake
    // timers are what make it measurable: without moving the clock, the mount value and the
    // post-save value are the same string and the assertion would hold for a form that never
    // re-read anything.
    // `shouldAdvanceTime` keeps the fake clock ticking with real time, which is what lets
    // `waitFor`'s own interval fire — a frozen clock deadlocks it.
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      vi.setSystemTime(new Date(2026, 7, 24, 9, 0));
      const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
      render(<AddFollowUpForm applicationId="app-1" />);

      const mountValue = (screen.getByLabelText("Datum") as HTMLInputElement).value;
      expect(mountValue).toBe("2026-08-24T09:00");

      vi.setSystemTime(new Date(2026, 7, 24, 14, 30));

      await user.selectOptions(screen.getByLabelText("Kanal"), "Email");
      await user.click(screen.getByRole("button", { name: SUBMIT }));

      await waitFor(() =>
        expect(screen.getByLabelText("Datum")).toHaveValue("2026-08-24T14:30"),
      );
    } finally {
      vi.useRealTimers();
    }
  });

  it("opens on the current time with nothing pre-filled", async () => {
    render(<AddFollowUpForm applicationId="app-1" />);

    expect(screen.getByLabelText("Datum")).not.toHaveValue("");
    expect(screen.getByLabelText(NOTE)).toHaveValue("");
  });
});
