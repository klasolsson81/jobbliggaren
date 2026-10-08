import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { createRef, useRef, type Ref } from "react";
import type { CodeProof, ReauthOutcome, ReauthRequestResult } from "@/lib/auth/reauth-action-state";
import { ReAuthCodeDialog, type ReauthDialogHandle, type ReauthHandOff } from "./reauth-code-dialog";

// #1740 — the shared re-authentication dialog, through a consumer of its own, in every D1 state
// design-reviewer bound: request, code, resend, the panels, the hand-offs, and a close. `render` is
// auto-wrapped in the Swedish catalogue. The code request is the consumer's (#1975), so no module is
// mocked: the harness hands the dialog its own.

const requestCodeMock = vi.fn<() => Promise<ReauthRequestResult>>();

const ADDRESS = "anna@exempel.se";
const FIRST = "first-challenge-under-test";
const SECOND = "second-challenge-under-test";
const BUDGET =
  "Ditt konto har nått gränsen för hur många koder som kan skickas per dygn. Vänta ett dygn och försök igen.";
const COOLDOWN = "En kod begärdes nyss för ditt konto. Vänta en minut och försök igen.";

type Operation = (proof: CodeProof) => Promise<ReauthOutcome<string>>;
type User = ReturnType<typeof userEvent.setup>;

function Harness({
  operation,
  onHandOff = () => {},
  onOpenChange,
  codeRecipientLabels,
  dialogRef,
  operationDisabled,
}: {
  operation: Operation;
  onHandOff?: (handOff: ReauthHandOff<string>) => void;
  onOpenChange?: (open: boolean) => void;
  codeRecipientLabels?: { request: string; pending: string; field: string };
  dialogRef?: Ref<ReauthDialogHandle>;
  operationDisabled?: boolean;
}) {
  const target = useRef<HTMLDivElement>(null);
  return (
    <>
      <ReAuthCodeDialog<string>
        dialogRef={dialogRef}
        trigger={<button type="button">Öppna</button>}
        title="Gör något"
        description="Det här gör operationen."
        currentEmail={ADDRESS}
        codeRecipientLabels={codeRecipientLabels}
        operationDisabled={operationDisabled}
        confirmLabel="Utför"
        pendingLabel="Utför…"
        cancelLabel="Avbryt"
        returnPath="/sida"
        requestCode={requestCodeMock}
        action={(proof) => operation(proof)}
        onHandOff={onHandOff}
        focusAfterHandOff={() => target.current?.focus()}
        onOpenChange={onOpenChange}
      />
      <div ref={target} tabIndex={-1} data-testid="hand-off-target" />
    </>
  );
}

async function open(user: User) {
  await user.click(screen.getByRole("button", { name: "Öppna" }));
  return screen.findByRole("dialog", { name: "Gör något" });
}

async function toCodeStep(user: User) {
  const dialog = await open(user);
  await user.click(within(dialog).getByRole("button", { name: "Skicka kod" }));
  return within(dialog).findByLabelText("Sexsiffrig kod");
}

async function submit(user: User, code = "123456") {
  await user.type(screen.getByLabelText("Sexsiffrig kod"), code);
  await user.click(screen.getByRole("button", { name: "Utför" }));
}

describe("ReAuthCodeDialog", () => {
  const operation = vi.fn<Operation>();

  beforeEach(() => {
    requestCodeMock.mockReset();
    requestCodeMock.mockResolvedValue({ ok: true, challengeId: FIRST });
    operation.mockReset();
    operation.mockResolvedValue({ ok: true, value: "done" });
  });

  it("names the operation and the address the code goes to, with nothing focusable before Avbryt", async () => {
    const user = userEvent.setup();
    render(<Harness operation={operation} />);

    const dialog = await open(user);

    expect(dialog).toHaveAccessibleDescription(
      `Det här gör operationen. För att bekräfta att det är du skickar vi en sexsiffrig kod till ${ADDRESS}.`
    );
    await waitFor(() => expect(within(dialog).getByRole("button", { name: "Avbryt" })).toHaveFocus());
  });

  it("moves to the code step once the code is sent, the field focused and described by the sent line", async () => {
    const user = userEvent.setup();
    render(<Harness operation={operation} />);

    const field = await toCodeStep(user);

    await waitFor(() => expect(field).toHaveFocus());
    expect(field).toHaveAccessibleDescription(
      `Vi har skickat en kod till ${ADDRESS}. Koden gäller i 15 minuter.`
    );
  });

  it("identifies the recipient on the controls when the consumer supplies the sole consequence description", async () => {
    const user = userEvent.setup();
    render(<Harness operation={operation} codeRecipientLabels={{
      request: `Skicka kod till ${ADDRESS}`,
      pending: `Skickar kod till ${ADDRESS}…`,
      field: `Kod till ${ADDRESS}`,
    }} />);
    const dialog = await open(user);
    expect(dialog).toHaveAccessibleDescription("Det här gör operationen.");
    await user.click(within(dialog).getByRole("button", { name: `Skicka kod till ${ADDRESS}` }));
    const field = await within(dialog).findByLabelText(`Kod till ${ADDRESS}`);
    await waitFor(() => expect(field).toHaveFocus());
    expect(dialog).toHaveAccessibleDescription("Det här gör operationen.");
    expect(field).not.toHaveAccessibleDescription(/Vi har skickat/);
    await user.type(field, "123456");
    await user.click(within(dialog).getByRole("button", { name: "Utför" }));
    expect(operation).toHaveBeenCalledExactlyOnceWith({ challengeId: FIRST, code: "123456" });
  });

  it("keeps the request step on a status, the message taking focus", async () => {
    const unavailable = "Det går inte att skicka någon kod just nu. Försök igen om några minuter.";
    requestCodeMock.mockResolvedValue({ ok: false, kind: "status", error: unavailable });
    const user = userEvent.setup();
    render(<Harness operation={operation} />);

    const dialog = await open(user);
    await user.click(within(dialog).getByRole("button", { name: "Skicka kod" }));

    const status = await within(dialog).findByRole("status");
    expect(status).toHaveTextContent(unavailable);
    await waitFor(() => expect(status).toHaveFocus());
    expect(within(dialog).getByRole("button", { name: "Skicka kod" })).toBeEnabled();
  });

  it("replaces the step with a panel when no code can be sent today", async () => {
    requestCodeMock.mockResolvedValue({ ok: false, kind: "terminal", error: BUDGET });
    const user = userEvent.setup();
    render(<Harness operation={operation} />);

    const dialog = await open(user);
    await user.click(within(dialog).getByRole("button", { name: "Skicka kod" }));

    const panel = await within(dialog).findByRole("status");
    expect(panel).toHaveTextContent(BUDGET);
    await waitFor(() => expect(panel).toHaveFocus());
    expect(within(dialog).queryByRole("button", { name: "Skicka kod" })).not.toBeInTheDocument();
    // The footer's "Stäng" beside the corner's.
    expect(within(dialog).getAllByRole("button", { name: "Stäng" })).toHaveLength(2);
  });

  it("sends a lapsed session to the login page and back", async () => {
    requestCodeMock.mockResolvedValue({ ok: false, kind: "notLoggedIn" });
    const user = userEvent.setup();
    render(<Harness operation={operation} />);

    const dialog = await open(user);
    await user.click(within(dialog).getByRole("button", { name: "Skicka kod" }));

    const panel = await within(dialog).findByRole("status");
    expect(panel).toHaveTextContent("Du är inte inloggad längre. Logga in igen och börja om.");
    expect(within(panel).getByRole("link", { name: "Logga in" })).toHaveAttribute(
      "href",
      "/logga-in?next=/sida"
    );
    await waitFor(() => expect(panel).toHaveFocus());
  });

  it("hands over a deployment without mail before any code exists, and closes", async () => {
    requestCodeMock.mockResolvedValue({ ok: false, kind: "refused" });
    const onHandOff = vi.fn();
    const user = userEvent.setup();
    render(<Harness operation={operation} onHandOff={onHandOff} />);

    const dialog = await open(user);
    await user.click(within(dialog).getByRole("button", { name: "Skicka kod" }));

    await waitFor(() => expect(onHandOff).toHaveBeenCalledWith({ kind: "refused" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    await waitFor(() => expect(screen.getByTestId("hand-off-target")).toHaveFocus());
  });

  it("refuses a malformed code without presenting it", async () => {
    const user = userEvent.setup();
    render(<Harness operation={operation} />);

    await toCodeStep(user);
    await submit(user, "12a");

    const field = screen.getByLabelText("Sexsiffrig kod");
    expect(screen.getByRole("alert")).toHaveTextContent("Koden är sex siffror.");
    expect(field).toHaveAttribute("aria-invalid", "true");
    await waitFor(() => expect(field).toHaveFocus());
    expect(operation).not.toHaveBeenCalled();
  });

  it("puts a wrong code on the field, which is typed again", async () => {
    operation.mockResolvedValue({
      ok: false,
      kind: "wrongCode",
      error: "Koden stämmer inte. Kontrollera siffrorna och försök igen.",
    });
    const user = userEvent.setup();
    render(<Harness operation={operation} />);

    await toCodeStep(user);
    await submit(user);

    expect(await screen.findByRole("alert")).toHaveTextContent("Koden stämmer inte.");
    const field = screen.getByLabelText("Sexsiffrig kod");
    expect(field).toHaveValue("");
    expect(field).toHaveAttribute("aria-invalid", "true");
    await waitFor(() => expect(field).toHaveFocus());
    expect(operation).toHaveBeenCalledWith({ challengeId: FIRST, code: "123456" });
  });

  it.each([
    ["burned", "Du har skrivit fel kod tre gånger, så koden går inte att använda längre. Skicka en ny kod och försök igen."],
    ["expired", "Koden går inte att använda längre. Skicka en ny kod och försök igen."],
  ] as const)("replaces the field and the primary with a panel on a %s code", async (reason, copy) => {
    operation.mockResolvedValue({ ok: false, kind: "deadCode", reason });
    const user = userEvent.setup();
    render(<Harness operation={operation} />);

    await toCodeStep(user);
    await submit(user);

    const panel = (await screen.findByText(copy)).closest('[role="status"]');
    await waitFor(() => expect(panel).toHaveFocus());
    expect(screen.queryByLabelText("Sexsiffrig kod")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Utför" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Skicka ny kod" })).toBeInTheDocument();
  });

  it("keeps the field on a status at verify, the message taking focus", async () => {
    const unavailable = "Det går inte att kontrollera koden just nu. Försök igen om några minuter.";
    operation.mockResolvedValue({ ok: false, kind: "status", error: unavailable });
    const user = userEvent.setup();
    render(<Harness operation={operation} />);

    await toCodeStep(user);
    await submit(user);

    const status = await screen.findByText(unavailable);
    expect(status).toHaveAttribute("role", "status");
    await waitFor(() => expect(status).toHaveFocus());
    expect(screen.getByLabelText("Sexsiffrig kod")).toBeInTheDocument();
  });

  it("starts again from the request step when the operation refused its own input", async () => {
    operation.mockResolvedValue({ ok: false, kind: "inputRefused", error: "Kontrollera uppgiften." });
    const user = userEvent.setup();
    render(<Harness operation={operation} />);

    await toCodeStep(user);
    await submit(user);

    expect(await screen.findByText("Kontrollera uppgiften.")).toHaveAttribute("role", "status");
    expect(await screen.findByRole("button", { name: "Skicka kod" })).toBeInTheDocument();
    expect(screen.queryByLabelText("Sexsiffrig kod")).not.toBeInTheDocument();
  });

  it.each([
    [{ ok: true, value: "done" }, { kind: "verified", value: "done" }],
    [
      { ok: false, kind: "operationRefused", error: "Inte gjort.", channel: "status", terminal: true },
      { kind: "operationRefused", error: "Inte gjort.", channel: "status", terminal: true },
    ],
    [
      { ok: false, kind: "operationRefused", error: "Upptagen.", channel: "field" },
      { kind: "operationRefused", error: "Upptagen.", channel: "field" },
    ],
    [
      { ok: false, kind: "outcomeUnknown", error: "Okänt." },
      { kind: "outcomeUnknown", error: "Okänt." },
    ],
    [
      { ok: false, kind: "refused", error: "Inget mejl." },
      { kind: "refused", error: "Inget mejl." },
    ],
  ] as const)("closes on a spent code and hands the outcome over, focus following (%#)", async (outcome, handOff) => {
    operation.mockResolvedValue(outcome);
    const onHandOff = vi.fn();
    const onOpenChange = vi.fn();
    const user = userEvent.setup();
    render(<Harness operation={operation} onHandOff={onHandOff} onOpenChange={onOpenChange} />);

    await toCodeStep(user);
    await submit(user);

    await waitFor(() => expect(onHandOff).toHaveBeenCalledWith(handOff));
    expect(onOpenChange).toHaveBeenLastCalledWith(false);
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    await waitFor(() => expect(screen.getByTestId("hand-off-target")).toHaveFocus());
  });

  it("never closes while the operation runs", async () => {
    let settle: (outcome: ReauthOutcome<string>) => void = () => {};
    operation.mockImplementation(() => new Promise((resolve) => (settle = resolve)));
    const user = userEvent.setup();
    render(<Harness operation={operation} />);

    await toCodeStep(user);
    await submit(user);
    await user.keyboard("{Escape}");

    const dialog = screen.getByRole("dialog", { name: "Gör något" });
    expect(within(dialog).getByRole("button", { name: "Utför…" })).toBeDisabled();
    expect(within(dialog).getByRole("button", { name: "Avbryt" })).toBeDisabled();
    // A transition left pending would outlive this test's unmount and leak into the next one.
    await act(async () => settle({ ok: false, kind: "status", error: "Försök igen." }));
  });

  it("gates requests and retained code submission on the consumer's fresh read, while leaving close available", async () => {
    const user = userEvent.setup();
    const { rerender } = render(<Harness operation={operation} operationDisabled />);
    const dialog = await open(user);
    const send = within(dialog).getByRole("button", { name: "Skicka kod" });
    expect(send).toBeDisabled();
    expect(within(dialog).getByRole("button", { name: "Avbryt" })).toBeEnabled();
    fireEvent.submit(send.closest("form")!);
    expect(requestCodeMock).not.toHaveBeenCalled();

    rerender(<Harness operation={operation} operationDisabled={false} />);
    await user.click(send);
    const code = await within(dialog).findByLabelText("Sexsiffrig kod");
    await user.type(code, "123456");
    rerender(<Harness operation={operation} operationDisabled />);
    const confirm = within(dialog).getByRole("button", { name: "Utför" });
    expect(code).toBeDisabled();
    expect(confirm).toBeDisabled();
    fireEvent.submit(confirm.closest("form")!);
    expect(operation).not.toHaveBeenCalled();
    await user.click(within(dialog).getByRole("button", { name: "Avbryt" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    await open(user);
    expect(screen.getByLabelText("Sexsiffrig kod")).toBeDisabled();
    expect(requestCodeMock).toHaveBeenCalledTimes(1);

    rerender(<Harness operation={operation} operationDisabled={false} />);
    await submit(user);
    await waitFor(() => expect(operation).toHaveBeenCalledExactlyOnceWith({ challengeId: FIRST, code: "123456" }));
  });

  it("closes through the parent's handle without running the action, then reuses the live challenge on reopening", async () => {
    const dialogRef = createRef<ReauthDialogHandle>();
    const onOpenChange = vi.fn();
    const user = userEvent.setup();
    render(<Harness operation={operation} dialogRef={dialogRef} onOpenChange={onOpenChange} />);

    const field = await toCodeStep(user);
    await user.type(field, "1234");
    expect(requestCodeMock).toHaveBeenCalledTimes(1);
    expect(dialogRef.current).not.toBeNull();

    act(() => dialogRef.current!.close());

    await waitFor(() => expect(screen.queryByRole("dialog", { name: "Gör något" })).not.toBeInTheDocument());
    expect(onOpenChange).toHaveBeenLastCalledWith(false);
    expect(operation).not.toHaveBeenCalled();
    const reopened = await open(user);
    expect(within(reopened).getByLabelText("Sexsiffrig kod")).toHaveValue("");
    expect(requestCodeMock).toHaveBeenCalledTimes(1);
    expect(operation).not.toHaveBeenCalled();

    await submit(user);

    expect(operation).toHaveBeenCalledExactlyOnceWith({ challengeId: FIRST, code: "123456" });
  });

  it("refuses the parent's close handle while a real code request or submission is pending", async () => {
    let settleRequest: (result: ReauthRequestResult) => void = () => {};
    requestCodeMock.mockImplementationOnce(() => new Promise((resolve) => (settleRequest = resolve)));
    let settleOperation: (outcome: ReauthOutcome<string>) => void = () => {};
    operation.mockImplementationOnce(() => new Promise((resolve) => (settleOperation = resolve)));
    const dialogRef = createRef<ReauthDialogHandle>();
    const onOpenChange = vi.fn();
    const user = userEvent.setup();
    render(<Harness operation={operation} dialogRef={dialogRef} onOpenChange={onOpenChange} />);
    const dialog = await open(user);
    await user.click(within(dialog).getByRole("button", { name: "Skicka kod" }));
    expect(requestCodeMock).toHaveBeenCalledTimes(1);
    expect(within(dialog).getByRole("button", { name: "Avbryt" })).toBeDisabled();
    expect(dialogRef.current).not.toBeNull();

    act(() => dialogRef.current!.close());

    expect(dialog).toBeInTheDocument();
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
    expect(operation).not.toHaveBeenCalled();
    await act(async () => settleRequest({ ok: true, challengeId: FIRST }));
    await within(dialog).findByLabelText("Sexsiffrig kod");
    await submit(user);
    expect(operation).toHaveBeenCalledExactlyOnceWith({ challengeId: FIRST, code: "123456" });
    expect(within(dialog).getByRole("button", { name: "Utför…" })).toBeDisabled();
    expect(within(dialog).getByRole("button", { name: "Avbryt" })).toBeDisabled();

    act(() => dialogRef.current!.close());

    expect(dialog).toBeInTheDocument();
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
    await act(async () => settleOperation({ ok: false, kind: "status", error: "Försök igen." }));
  });

  it("resets the code and the message on a user close, and re-opens on the live code's step", async () => {
    operation.mockResolvedValue({ ok: false, kind: "wrongCode", error: "Koden stämmer inte." });
    const user = userEvent.setup();
    render(<Harness operation={operation} />);

    await toCodeStep(user);
    await submit(user);
    await screen.findByRole("alert");
    await user.type(screen.getByLabelText("Sexsiffrig kod"), "1234");
    await user.click(screen.getByRole("button", { name: "Avbryt" }));
    await waitFor(() => expect(screen.getByRole("button", { name: "Öppna" })).toHaveFocus());

    const dialog = await open(user);

    const field = within(dialog).getByLabelText("Sexsiffrig kod");
    expect(field).toHaveValue("");
    await waitFor(() => expect(field).toHaveFocus());
    expect(within(dialog).queryByRole("alert")).not.toBeInTheDocument();
    expect(requestCodeMock).toHaveBeenCalledTimes(1);
  });

  it("does not re-open on a dead code's step", async () => {
    operation.mockResolvedValue({ ok: false, kind: "deadCode", reason: "burned" });
    const user = userEvent.setup();
    render(<Harness operation={operation} />);

    await toCodeStep(user);
    await submit(user);
    await screen.findByText(/Du har skrivit fel kod tre gånger/);
    await user.click(screen.getByRole("button", { name: "Avbryt" }));

    const dialog = await open(user);

    expect(within(dialog).getByRole("button", { name: "Skicka kod" })).toBeInTheDocument();
  });

  it("never puts the challenge id in the page", async () => {
    const user = userEvent.setup();
    render(<Harness operation={operation} />);

    await toCodeStep(user);

    expect(document.body.innerHTML).not.toContain(FIRST);
  });

  describe("on the clock", () => {
    beforeEach(() => {
      vi.useFakeTimers({ shouldAdvanceTime: true });
    });

    afterEach(() => {
      vi.useRealTimers();
    });

    const clockedUser = () => userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    const resend = () => screen.getByRole("button", { name: "Skicka ny kod" });
    const wait = (seconds: number) =>
      act(() => {
        vi.advanceTimersByTime(seconds * 1000);
      });

    it("keeps an otherwise due resend unavailable until the consumer's fresh read completes", async () => {
      const user = clockedUser();
      const { rerender } = render(<Harness operation={operation} />);
      await toCodeStep(user);
      await wait(60);
      expect(resend()).toBeEnabled();
      rerender(<Harness operation={operation} operationDisabled />);
      expect(resend()).toBeDisabled();
      await user.click(resend());
      expect(requestCodeMock).toHaveBeenCalledTimes(1);
      expect(operation).not.toHaveBeenCalled();
      rerender(<Harness operation={operation} operationDisabled={false} />);
      requestCodeMock.mockResolvedValueOnce({ ok: true, challengeId: SECOND });
      await user.click(resend());
      await submit(user);
      await waitFor(() => expect(operation).toHaveBeenCalledExactlyOnceWith({ challengeId: SECOND, code: "123456" }));
    });

    it("refuses the parent's close handle while an enabled resend runs, then uses the replacement challenge", async () => {
      const dialogRef = createRef<ReauthDialogHandle>();
      const onOpenChange = vi.fn();
      const user = clockedUser();
      render(<Harness operation={operation} dialogRef={dialogRef} onOpenChange={onOpenChange} />);
      await toCodeStep(user);
      await wait(60);
      expect(resend()).toBeEnabled();
      let settle: (result: ReauthRequestResult) => void = () => {};
      requestCodeMock.mockImplementationOnce(() => new Promise((resolve) => (settle = resolve)));
      try {
        await user.click(resend());
        const dialog = screen.getByRole("dialog", { name: "Gör något" });
        expect(requestCodeMock).toHaveBeenCalledTimes(2);
        expect(await within(dialog).findByRole("button", { name: "Skickar…" })).toBeDisabled();
        expect(dialogRef.current).not.toBeNull();

        act(() => dialogRef.current!.close());

        expect(dialog).toBeInTheDocument();
        expect(onOpenChange).not.toHaveBeenCalledWith(false);
        expect(operation).not.toHaveBeenCalled();
        await act(async () => settle({ ok: true, challengeId: SECOND }));
        await within(dialog).findByText("Vi har skickat en ny kod. Skriv in koden från det senaste mejlet.");
        await submit(user);

        expect(operation).toHaveBeenCalledExactlyOnceWith({ challengeId: SECOND, code: "123456" });
      } finally {
        await act(async () => settle({ ok: true, challengeId: SECOND }));
      }
    });

    it("counts the resend down outside the live region, then sends a code the next attempt uses", async () => {
      const user = clockedUser();
      render(<Harness operation={operation} />);

      await toCodeStep(user);

      expect(resend()).toBeDisabled();
      expect(resend()).toHaveAccessibleDescription("En ny kod ersätter den förra.");
      const hint = screen.getByText("Du kan skicka en ny kod om 60 sekunder.");
      expect(hint.closest('[role="status"]')).toBeNull();

      await wait(60);
      expect(resend()).toBeEnabled();

      requestCodeMock.mockResolvedValue({ ok: true, challengeId: SECOND });
      await user.click(resend());

      expect(
        await screen.findByText("Vi har skickat en ny kod. Skriv in koden från det senaste mejlet.")
      ).toBeInTheDocument();
      await waitFor(() => expect(screen.getByLabelText("Sexsiffrig kod")).toHaveFocus());
      await submit(user);
      await waitFor(() =>
        expect(operation).toHaveBeenCalledWith({ challengeId: SECOND, code: "123456" })
      );
    });

    it("restarts the count on a cooldown refusal, and the code already mailed stays usable", async () => {
      const user = clockedUser();
      render(<Harness operation={operation} />);

      await toCodeStep(user);
      await wait(60);
      requestCodeMock.mockResolvedValue({
        ok: false,
        kind: "status",
        error: COOLDOWN,
        cooldown: true,
      });
      await user.click(resend());

      expect(await screen.findByText(COOLDOWN)).toBeInTheDocument();
      // The notice lands before the transition's pending flag clears the "Skickar…" label.
      expect(await screen.findByRole("button", { name: "Skicka ny kod" })).toBeDisabled();
      expect(screen.getByText("Du kan skicka en ny kod om 60 sekunder.")).toBeInTheDocument();
      await submit(user);
      await waitFor(() =>
        expect(operation).toHaveBeenCalledWith({ challengeId: FIRST, code: "123456" })
      );
    });

    it("blocks the resend for the day on a spent budget, and the live code stays usable", async () => {
      const user = clockedUser();
      render(<Harness operation={operation} />);

      await toCodeStep(user);
      await wait(60);
      requestCodeMock.mockResolvedValue({ ok: false, kind: "terminal", error: BUDGET });
      await user.click(resend());

      expect(await screen.findByText(BUDGET)).toBeInTheDocument();
      expect(await screen.findByRole("button", { name: "Skicka ny kod" })).toBeDisabled();
      expect(screen.queryByText(/Du kan skicka en ny kod om/)).not.toBeInTheDocument();
      expect(screen.getByLabelText("Sexsiffrig kod")).toBeEnabled();
    });

    it("makes the resend the way on once the code is dead and the count has run out", async () => {
      operation.mockResolvedValue({ ok: false, kind: "deadCode", reason: "expired" });
      const user = clockedUser();
      render(<Harness operation={operation} />);

      await toCodeStep(user);
      await submit(user);
      await screen.findByText("Koden går inte att använda längre. Skicka en ny kod och försök igen.");
      expect(resend()).toHaveAttribute("data-variant", "outline");

      await wait(60);

      expect(resend()).toHaveAttribute("data-variant", "default");
    });

    it("re-opens on the code step with what is left of the count, inside the code's lifetime", async () => {
      const issuedAt = Date.UTC(2026, 0, 1);
      const clock = vi.spyOn(Date, "now").mockReturnValue(issuedAt);
      try {
        const user = clockedUser();
        render(<Harness operation={operation} />);

        await toCodeStep(user);
        await user.click(screen.getByRole("button", { name: "Avbryt" }));
        clock.mockReturnValue(issuedAt + 20_000);
        await open(user);

        expect(screen.getByLabelText("Sexsiffrig kod")).toBeInTheDocument();
        expect(screen.getByText("Du kan skicka en ny kod om 40 sekunder.")).toBeInTheDocument();
      } finally {
        clock.mockRestore();
      }
    });

    it("re-opens on the request step once the code's lifetime has passed", async () => {
      const user = clockedUser();
      render(<Harness operation={operation} />);

      await toCodeStep(user);
      await user.click(screen.getByRole("button", { name: "Avbryt" }));
      vi.setSystemTime(Date.now() + 15 * 60 * 1000);
      const dialog = await open(user);

      expect(within(dialog).getByRole("button", { name: "Skicka kod" })).toBeInTheDocument();
      expect(within(dialog).queryByLabelText("Sexsiffrig kod")).not.toBeInTheDocument();
    });
  });
});
