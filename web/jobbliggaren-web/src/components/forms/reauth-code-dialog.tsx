"use client";

import {
  type FormEvent,
  type ReactNode,
  type RefObject,
  useEffect,
  useId,
  useRef,
  useState,
  useTransition,
} from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { LoginFormMessage } from "@/components/auth/login-form-message";
import { STANDALONE_LINK } from "@/components/auth/mail-link";
import { CodeField } from "@/components/forms/code-field";
import { PendingLabel } from "@/components/forms/pending-label";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog";
import type { MessageChannel } from "@/lib/auth/challenge-action-state";
import { codeInputSchema } from "@/lib/auth/challenge-schemas";
import { CODE_PHASE_MAX_AGE_SECONDS, RESEND_COOLDOWN_SECONDS } from "@/lib/auth/login-flow";
import type { CodeProof, ReauthOutcome } from "@/lib/auth/reauth-action-state";
import { requestReauthCode } from "@/lib/auth/reauth-actions";
import { useCountdown } from "@/lib/hooks/use-countdown";

// Re-authentication by a code to the account's own address (#1740, ADR 0142 D5), as design-reviewer
// bound it in the form round: the dialog ONLY re-authenticates (Klas 2026-09-22). It asks for a code,
// takes the code, and hands the consumer's operation the proof; the operation verifies the code and
// acts in one Server Action, so the grant between them never reaches the browser.
//
// The challenge id is held here, in the component that stays mounted with the card, never inside the
// dialog content that unmounts on close: a close and a re-open inside the code's lifetime land back on
// the code step instead of meeting the cooldown with a code already mailed. It lives only in this
// state (security-auditor, #1740 S1 (b)–(e)) and is dropped once it cannot be verified any more: after
// a verify that answered 200, on a 410, after the code's lifetime, and when this component unmounts.

/** What the dialog hands its consumer when it closes on its own; the consumer renders the outcome. */
export type ReauthHandOff<T> =
  | { kind: "verified"; value: T }
  | { kind: "operationRefused"; error: string; channel: MessageChannel; terminal?: true }
  | { kind: "outcomeUnknown"; error: string }
  | { kind: "refused"; error?: string };

type Challenge<C> = {
  /** Null once the backend has answered 410 for it: the step stays, but nothing is left to verify. */
  id: string | null;
  sentAt: number;
  /** What the consumer confirmed before the code was asked for (delete's typed address). */
  context: C;
};

type Panel = { kind: "terminal"; message: string } | { kind: "notLoggedIn" };

type FocusTarget = "code" | "message" | "panel" | "resend";

const nowSeconds = () => Math.floor(Date.now() / 1000);

type ReAuthCodeDialogProps<T, C> = {
  /** The element that opens the dialog; its own `onClick` may `preventDefault()` to keep it closed. */
  trigger: ReactNode;
  title: string;
  /** What the operation does and what it costs. Plain text: nothing focusable before the first field. */
  description: string;
  currentEmail: string;
  confirmLabel: string;
  pendingLabel: string;
  cancelLabel: string;
  variant?: "default" | "destructive";
  /** Fields the consumer asks for before a code is sent (delete's typed address). */
  requestFields?: ReactNode;
  /** The code field's hint (plain text: the hint is read as a description). */
  codeHintExtra?: ReactNode;
  /** Appended to the daily-budget message: the way on when no more codes can be sent today. */
  terminalExtra?: ReactNode;
  action: (proof: CodeProof, context: C) => Promise<ReauthOutcome<T>>;
  onHandOff: (handOff: ReauthHandOff<T>) => void;
  /** Moves focus to the consumer's target once a hand-off has closed the dialog. */
  focusAfterHandOff: () => void;
  onOpenChange?: (open: boolean) => void;
} & ([C] extends [undefined]
  ? { onBeforeRequest?: undefined }
  : {
      /**
       * Validates `requestFields` when "Skicka kod" is pressed and returns what the operation needs
       * from them, or null to stay (the consumer shows its own message on its field).
       */
      onBeforeRequest: () => C | null;
    });

export function ReAuthCodeDialog<T, C = undefined>({
  trigger,
  title,
  description,
  currentEmail,
  confirmLabel,
  pendingLabel,
  cancelLabel,
  variant = "default",
  requestFields,
  onBeforeRequest,
  codeHintExtra,
  terminalExtra,
  action,
  onHandOff,
  focusAfterHandOff,
  onOpenChange,
}: ReAuthCodeDialogProps<T, C>) {
  const t = useTranslations("settings");
  const tp = useTranslations("pages");
  const tc = useTranslations("common");

  const [open, setOpen] = useState(false);
  const [challenge, setChallenge] = useState<Challenge<C> | null>(null);
  const [step, setStep] = useState<"request" | "code">("request");
  const [code, setCode] = useState("");
  const [message, setMessage] = useState<{ text: string; channel: MessageChannel } | null>(null);
  const [panel, setPanel] = useState<Panel | null>(null);
  const [dead, setDead] = useState<"expired" | "burned" | null>(null);
  const [resendNotice, setResendNotice] = useState<string | null>(null);
  const [resendBlocked, setResendBlocked] = useState(false);
  // The resend countdown: when it started (the last send, or a refusal inside the server's window),
  // and how many seconds it had left then. Counted here, in events, so the render stays pure.
  const [resendClock, setResendClock] = useState({ from: 0, seconds: 0 });
  const [isPending, startTransition] = useTransition();
  const [isResending, startResend] = useTransition();

  const handingOff = useRef(false);
  const pendingFocus = useRef<FocusTarget | null>(null);
  const codeRef = useRef<HTMLInputElement>(null);
  const messageRef = useRef<HTMLParagraphElement>(null);
  const panelRef = useRef<HTMLDivElement>(null);
  const resendRef = useRef<HTMLDivElement>(null);

  const codeId = useId();
  const hintId = useId();
  const sentLineId = useId();
  const messageId = useId();
  const consequenceId = useId();

  // Focus follows the state it belongs to, once that state is on screen.
  useEffect(() => {
    const target = pendingFocus.current;
    if (!target) return;
    pendingFocus.current = null;
    const refs = { code: codeRef, message: messageRef, panel: panelRef, resend: resendRef };
    refs[target].current?.focus();
  });

  function resetView() {
    setCode("");
    setMessage(null);
    setPanel(null);
    setDead(null);
    setResendNotice(null);
    setResendBlocked(false);
  }

  function handleOpenChange(next: boolean) {
    if (isPending || isResending) return; // never close mid-operation
    if (next) {
      const live =
        challenge !== null &&
        challenge.id !== null &&
        nowSeconds() - challenge.sentAt < CODE_PHASE_MAX_AGE_SECONDS;
      if (!live) setChallenge(null);
      setStep(live ? "code" : "request");
      if (live) {
        const from = resendClock.from;
        setResendClock({
          from,
          seconds: Math.max(0, RESEND_COOLDOWN_SECONDS - (nowSeconds() - from)),
        });
      }
    }
    resetView();
    setOpen(next);
    onOpenChange?.(next);
  }

  function handOff(result: ReauthHandOff<T>) {
    setChallenge(null);
    resetView();
    handingOff.current = true;
    setOpen(false);
    onOpenChange?.(false);
    onHandOff(result);
  }

  function showMessage(text: string, channel: MessageChannel) {
    setMessage({ text, channel });
    pendingFocus.current = channel === "field" ? "code" : "message";
  }

  function onRequest(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    let context: C;
    if (onBeforeRequest) {
      const confirmed = onBeforeRequest();
      if (confirmed === null) return;
      context = confirmed;
    } else {
      context = undefined as C;
    }
    setMessage(null);
    startTransition(async () => {
      const result = await requestReauthCode();
      if (result.ok) {
        const sentAt = nowSeconds();
        setChallenge({ id: result.challengeId, sentAt, context });
        setResendClock({ from: sentAt, seconds: RESEND_COOLDOWN_SECONDS });
        setStep("code");
        pendingFocus.current = "code";
        return;
      }
      switch (result.kind) {
        case "status":
          showMessage(result.error, "status");
          return;
        case "terminal":
          setPanel({ kind: "terminal", message: result.error });
          pendingFocus.current = "panel";
          return;
        case "notLoggedIn":
          setPanel({ kind: "notLoggedIn" });
          pendingFocus.current = "panel";
          return;
        case "refused":
          handOff({ kind: "refused" });
          return;
      }
    });
  }

  function onSubmitCode(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const current = challenge;
    if (!current?.id) return;
    const parsed = codeInputSchema.safeParse(code);
    if (!parsed.success) {
      showMessage(tp("auth.passwordless.code.malformedCode"), "field");
      return;
    }
    const proof = { challengeId: current.id, code: parsed.data };
    setMessage(null);
    startTransition(async () => {
      const outcome = await action(proof, current.context);
      if (outcome.ok) {
        handOff({ kind: "verified", value: outcome.value });
        return;
      }
      switch (outcome.kind) {
        case "wrongCode":
          // The code is never echoed back, so it is typed again after a miss.
          setCode("");
          showMessage(outcome.error, "field");
          return;
        case "deadCode":
          setChallenge({ ...current, id: null });
          setCode("");
          setDead(outcome.reason);
          pendingFocus.current = "panel";
          return;
        case "status":
          showMessage(outcome.error, "status");
          return;
        case "notLoggedIn":
          setPanel({ kind: "notLoggedIn" });
          pendingFocus.current = "panel";
          return;
        case "inputRefused":
          setChallenge(null);
          setCode("");
          setStep("request");
          showMessage(outcome.error, "status");
          return;
        case "operationRefused":
          handOff(
            outcome.terminal
              ? { kind: "operationRefused", error: outcome.error, channel: outcome.channel, terminal: true }
              : { kind: "operationRefused", error: outcome.error, channel: outcome.channel }
          );
          return;
        case "outcomeUnknown":
          handOff({ kind: "outcomeUnknown", error: outcome.error });
          return;
        case "refused":
          handOff(outcome.error ? { kind: "refused", error: outcome.error } : { kind: "refused" });
          return;
      }
    });
  }

  function onResend() {
    const current = challenge;
    if (!current) return;
    setResendNotice(null);
    startResend(async () => {
      const result = await requestReauthCode();
      if (result.ok) {
        // A new code replaces the previous one on the server, so the old id is dead from here on.
        const sentAt = nowSeconds();
        setChallenge({ id: result.challengeId, sentAt, context: current.context });
        setResendClock({ from: sentAt, seconds: RESEND_COOLDOWN_SECONDS });
        setDead(null);
        setMessage(null);
        setCode("");
        setResendNotice(t("account.reauth.resendReceipt"));
        pendingFocus.current = "code";
        return;
      }
      switch (result.kind) {
        case "status":
          // A refused request writes no challenge, so the code already mailed stays usable.
          if (result.cooldown) {
            setResendClock({ from: nowSeconds(), seconds: RESEND_COOLDOWN_SECONDS });
          }
          setResendNotice(result.error);
          pendingFocus.current = current.id ? "code" : "resend";
          return;
        case "terminal":
          setResendBlocked(true);
          setResendNotice(result.error);
          pendingFocus.current = "resend";
          return;
        case "notLoggedIn":
          setPanel({ kind: "notLoggedIn" });
          pendingFocus.current = "panel";
          return;
        case "refused":
          handOff({ kind: "refused" });
          return;
      }
    });
  }

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogTrigger asChild>{trigger}</DialogTrigger>
      <DialogContent
        // WCAG 1.4.10 / 1.4.4: at 200 % zoom a short viewport is 360 CSS px tall, and the code step is
        // taller than that; the content scrolls rather than cutting off its title or its primary.
        className="max-h-[calc(100dvh-2rem)] overflow-y-auto"
        onOpenAutoFocus={(event) => {
          if (step === "code") {
            event.preventDefault();
            codeRef.current?.focus();
          }
        }}
        onCloseAutoFocus={(event) => {
          if (!handingOff.current) return; // a user close returns focus to the trigger
          handingOff.current = false;
          event.preventDefault();
          focusAfterHandOff();
        }}
      >
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          <DialogDescription asChild>
            <div className="flex flex-col gap-2 [overflow-wrap:anywhere]">
              <p>{description}</p>
              {panel === null && step === "request" && (
                <p>{t("account.reauth.request", { email: currentEmail })}</p>
              )}
              {panel === null && step === "code" && (
                <p id={sentLineId}>{t("account.reauth.sent", { email: currentEmail })}</p>
              )}
            </div>
          </DialogDescription>
        </DialogHeader>

        {panel !== null ? (
          <>
            <div
              ref={panelRef}
              tabIndex={-1}
              role="status"
              className="flex flex-col gap-2 text-body-sm text-text-primary [overflow-wrap:anywhere]"
            >
              {panel.kind === "terminal" ? (
                <p>
                  {panel.message}
                  {terminalExtra ? <> {terminalExtra}</> : null}
                </p>
              ) : (
                <>
                  <p>{t("account.reauth.notLoggedIn")}</p>
                  <p>
                    <Link href="/logga-in?next=/mina-sidor" className={STANDALONE_LINK}>
                      {t("account.reauth.toLogin")}
                    </Link>
                  </p>
                </>
              )}
            </div>
            <DialogFooter>
              <Button
                type="button"
                variant="ghost"
                className="max-md:h-11"
                onClick={() => handleOpenChange(false)}
              >
                {tc("dialog.close")}
              </Button>
            </DialogFooter>
          </>
        ) : step === "request" ? (
          <form onSubmit={onRequest} noValidate className="flex flex-col gap-4">
            {requestFields && (
              <fieldset disabled={isPending} className="m-0 flex min-w-0 flex-col gap-4 border-0 p-0">
                {requestFields}
              </fieldset>
            )}
            {message && (
              <LoginFormMessage
                id={messageId}
                message={message.text}
                channel={message.channel}
                statusRef={messageRef}
              />
            )}
            <DialogFooter>
              <Button
                type="button"
                variant="ghost"
                disabled={isPending}
                className="max-md:h-11"
                onClick={() => handleOpenChange(false)}
              >
                {cancelLabel}
              </Button>
              <Button type="submit" disabled={isPending} className="max-md:h-11">
                <PendingLabel
                  pending={isPending}
                  idle={t("account.reauth.send")}
                  busy={t("account.reauth.sending")}
                />
              </Button>
            </DialogFooter>
          </form>
        ) : (
          <form onSubmit={onSubmitCode} noValidate className="flex flex-col gap-4">
            {dead === null ? (
              <CodeField
                id={codeId}
                hintId={hintId}
                label={tp("auth.passwordless.code.codeLabel")}
                hint={codeHintExtra}
                invalid={message?.channel === "field"}
                errorId={messageId}
                leadingDescriptionId={sentLineId}
                inputRef={codeRef}
                value={code}
                onValueChange={setCode}
              />
            ) : (
              <div ref={panelRef} tabIndex={-1} role="status" aria-live="polite">
                <p className="text-body text-text-primary">
                  {dead === "burned"
                    ? t("account.reauth.burned")
                    : tp("auth.passwordless.code.expired")}
                </p>
              </div>
            )}
            {message && (
              <LoginFormMessage
                id={messageId}
                message={message.text}
                channel={message.channel}
                statusRef={messageRef}
              />
            )}
            {challenge !== null && (
              <ResendBlock
                clock={resendClock}
                primary={dead !== null}
                blocked={resendBlocked}
                pending={isResending}
                notice={resendNotice}
                noticeRef={resendRef}
                consequenceId={consequenceId}
                extra={resendBlocked ? terminalExtra : null}
                onResend={onResend}
              />
            )}
            <DialogFooter>
              <Button
                type="button"
                variant="ghost"
                disabled={isPending}
                className="max-md:h-11"
                onClick={() => handleOpenChange(false)}
              >
                {cancelLabel}
              </Button>
              {dead === null && (
                <Button type="submit" variant={variant} disabled={isPending} className="max-md:h-11">
                  <PendingLabel pending={isPending} idle={confirmLabel} busy={pendingLabel} />
                </Button>
              )}
            </DialogFooter>
          </form>
        )}
      </DialogContent>
    </Dialog>
  );
}

// "Skicka ny kod": the resend form of the login page's code step (the countdown OUTSIDE the live
// region, the receipt in `role="status"`, the cost stated before the press), minus its form: a submit
// here would compete with the code field's Enter, which must always press the consumer's primary.
function ResendBlock({
  clock,
  primary,
  blocked,
  pending,
  notice,
  noticeRef,
  consequenceId,
  extra,
  onResend,
}: {
  clock: { from: number; seconds: number };
  /** True once the code is dead and a new one is the way on. */
  primary: boolean;
  /** The daily budget is spent: no press can succeed today. */
  blocked: boolean;
  pending: boolean;
  notice: string | null;
  noticeRef: RefObject<HTMLDivElement | null>;
  consequenceId: string;
  extra: ReactNode;
  onResend: () => void;
}) {
  const tp = useTranslations("pages");
  const seconds = useCountdown(clock.seconds, clock.from);
  const cooling = seconds > 0;

  return (
    <div className="flex flex-col gap-2">
      <div>
        <Button
          type="button"
          variant={primary && !cooling && !blocked ? "default" : "outline"}
          disabled={pending || cooling || blocked}
          aria-describedby={consequenceId}
          onClick={onResend}
          className="max-md:h-11"
        >
          {pending
            ? tp("auth.passwordless.code.resend.sending")
            : tp("auth.passwordless.code.resend.button")}
        </Button>
      </div>
      {cooling && !blocked && (
        <p className="text-body-sm text-text-primary">
          {tp("auth.passwordless.code.resend.cooldownHint", { seconds })}
        </p>
      )}
      <div ref={noticeRef} tabIndex={-1} role="status" aria-live="polite">
        {notice && (
          <p className="text-body-sm text-text-primary [overflow-wrap:anywhere]">
            {notice}
            {extra ? <> {extra}</> : null}
          </p>
        )}
      </div>
      <p id={consequenceId} className="text-body-sm text-text-primary">
        {tp("auth.passwordless.code.resend.consequence")}
      </p>
    </div>
  );
}
