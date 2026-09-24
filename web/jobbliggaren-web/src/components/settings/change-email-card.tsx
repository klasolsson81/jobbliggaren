"use client";

// Change-email by two codes (#1740, ADR 0142 D5), as Klas ruled on 2026-09-22: the shared dialog only
// re-authenticates, by a code to the current address, and closes; this card then takes the code mailed
// to the NEW address in a step of its own. The new address is checked when "Fortsätt" is pressed,
// before the dialog opens, so no code is spent on an address the check refuses.
//
// The change challenge lives only in this card's state (security-auditor, #1740 S1): it is dropped on a
// confirm that succeeds, on a dead code, on "Börja om", after its lifetime and on unmount.

import {
  type FormEvent,
  type MouseEvent,
  type ReactNode,
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
import { ReAuthCodeDialog, type ReauthHandOff } from "@/components/forms/reauth-code-dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { confirmEmailChangeAction, requestEmailChangeAction } from "@/lib/actions/me";
import type { MessageChannel } from "@/lib/auth/challenge-action-state";
import { codeInputSchema } from "@/lib/auth/challenge-schemas";
import { CODE_PHASE_MAX_AGE_SECONDS } from "@/lib/auth/login-flow";
import { checkNewAddress, NEW_ADDRESS_REFUSAL_COPY } from "@/lib/auth/new-address";

type ChangeChallenge = { id: string; sentAt: number; address: string };

type View =
  | { step: "address" }
  /** No request can succeed today: the panel takes the place of the field and "Fortsätt". */
  | { step: "addressClosed"; message: string }
  | { step: "code"; challenge: ChangeChallenge }
  /** The change code can no longer be used, so "Börja om" is the way on. */
  | { step: "codeDead"; message: string }
  /** The confirm left and nothing readable came back: the address may have changed. */
  | { step: "unknown"; message: string }
  | { step: "notLoggedIn" }
  /** No mail can be delivered on this deployment. */
  | { step: "mailOff"; message?: string };

type FocusTarget = "field" | "code" | "message" | "panel";

const nowSeconds = () => Math.floor(Date.now() / 1000);

// The delivered text-link form of a control that changes state (`change-email-button.tsx`).
const START_OVER_LINK =
  "-my-2 h-auto px-0 py-2 text-brand-700 underline underline-offset-2 max-md:-my-2.5 max-md:py-2.5";

export function ChangeEmailCard({ currentEmail }: { currentEmail: string }) {
  const t = useTranslations("settings");
  const tp = useTranslations("pages");

  const [view, setView] = useState<View>({ step: "address" });
  const [input, setInput] = useState("");
  // The address "Fortsätt" last admitted: the dialog's description names it and its action sends it.
  const [pendingAddress, setPendingAddress] = useState("");
  const [code, setCode] = useState("");
  const [message, setMessage] = useState<{ text: string; channel: MessageChannel } | null>(null);
  const [isPending, startTransition] = useTransition();

  const pendingFocus = useRef<FocusTarget | null>(null);
  const handOffFocus = useRef<FocusTarget | null>(null);
  const fieldRef = useRef<HTMLInputElement>(null);
  const codeRef = useRef<HTMLInputElement>(null);
  const messageRef = useRef<HTMLParagraphElement>(null);
  const panelRef = useRef<HTMLDivElement>(null);

  const fieldId = useId();
  const fieldHintId = useId();
  const codeId = useId();
  const codeHintId = useId();
  const codeStepId = useId();
  const messageId = useId();

  function focus(target: FocusTarget) {
    const refs = { field: fieldRef, code: codeRef, message: messageRef, panel: panelRef };
    refs[target].current?.focus();
  }

  // Focus follows the state it belongs to, once that state is on screen. A hand-off from the dialog
  // is the exception: it is focused from the dialog's close, after its focus trap has let go.
  useEffect(() => {
    const target = pendingFocus.current;
    if (!target) return;
    pendingFocus.current = null;
    focus(target);
  });

  function onContinue(event: MouseEvent<HTMLButtonElement>) {
    const verdict = checkNewAddress(input, currentEmail);
    if (!verdict.ok) {
      event.preventDefault(); // the dialog stays closed, and the form unsent
      setMessage({ text: t(NEW_ADDRESS_REFUSAL_COPY[verdict.reason]), channel: "field" });
      pendingFocus.current = "field";
      return;
    }
    setMessage(null);
    setPendingAddress(verdict.address);
  }

  function onHandOff(handOff: ReauthHandOff<{ challengeId: string }>) {
    switch (handOff.kind) {
      case "verified":
        setView({
          step: "code",
          challenge: { id: handOff.value.challengeId, sentAt: nowSeconds(), address: pendingAddress },
        });
        setCode("");
        setMessage(null);
        handOffFocus.current = "code";
        return;
      case "operationRefused":
        if (handOff.terminal) {
          setView({ step: "addressClosed", message: handOff.error });
          handOffFocus.current = "panel";
          return;
        }
        setMessage({ text: handOff.error, channel: handOff.channel });
        handOffFocus.current = handOff.channel === "field" ? "field" : "message";
        return;
      case "outcomeUnknown":
        setView({ step: "unknown", message: handOff.error });
        handOffFocus.current = "panel";
        return;
      case "refused":
        setView(handOff.error ? { step: "mailOff", message: handOff.error } : { step: "mailOff" });
        handOffFocus.current = "panel";
        return;
    }
  }

  function focusAfterHandOff() {
    const target = handOffFocus.current;
    handOffFocus.current = null;
    if (target) focus(target);
  }

  function toAddressStep(fieldMessage: string | null) {
    setView({ step: "address" });
    setPendingAddress("");
    setCode("");
    setMessage(fieldMessage === null ? null : { text: fieldMessage, channel: "field" });
    pendingFocus.current = "field";
  }

  function toDead(text: string) {
    setView({ step: "codeDead", message: text });
    setCode("");
    setMessage(null);
    pendingFocus.current = "panel";
  }

  function onConfirm(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (view.step !== "code") return;
    const { challenge } = view;
    if (nowSeconds() - challenge.sentAt >= CODE_PHASE_MAX_AGE_SECONDS) {
      toDead(t("account.changeEmail.deadCode"));
      return;
    }
    const parsed = codeInputSchema.safeParse(code);
    if (!parsed.success) {
      setMessage({ text: tp("auth.passwordless.code.malformedCode"), channel: "field" });
      pendingFocus.current = "code";
      return;
    }
    setMessage(null);
    startTransition(async () => {
      const outcome = await confirmEmailChangeAction(challenge.address, {
        challengeId: challenge.id,
        code: parsed.data,
      });
      if (outcome.ok) {
        // The action re-set the session cookie, so the page re-renders with the new address.
        setView({ step: "address" });
        setInput("");
        setPendingAddress("");
        setCode("");
        setMessage({
          text: t("account.changeEmail.success", { newEmail: challenge.address }),
          channel: "status",
        });
        pendingFocus.current = "message";
        return;
      }
      switch (outcome.kind) {
        case "wrongCode":
          // The code is never echoed back, so it is typed again after a miss.
          setCode("");
          setMessage({ text: outcome.error, channel: "field" });
          pendingFocus.current = "code";
          return;
        case "deadCode":
          toDead(
            t(outcome.reason === "burned" ? "account.changeEmail.burned" : "account.changeEmail.deadCode")
          );
          return;
        case "status":
          setMessage({ text: outcome.error, channel: "status" });
          pendingFocus.current = "message";
          return;
        case "notLoggedIn":
          setView({ step: "notLoggedIn" });
          pendingFocus.current = "panel";
          return;
        case "inputRefused":
          toAddressStep(outcome.error);
          return;
        case "operationRefused":
          if (outcome.channel === "field") {
            toAddressStep(outcome.error);
          } else if (outcome.terminal) {
            toDead(outcome.error);
          } else {
            setMessage({ text: outcome.error, channel: "status" });
            pendingFocus.current = "message";
          }
          return;
        case "outcomeUnknown":
          setView({ step: "unknown", message: outcome.error });
          pendingFocus.current = "panel";
          return;
      }
    });
  }

  const title = <h2 className="jp-card__title">{t("account.changeEmail.title")}</h2>;
  const current = (
    <p className="text-body-sm text-text-primary [overflow-wrap:anywhere]">
      {t("account.changeEmail.current", { email: currentEmail })}
    </p>
  );
  const slot = message && (
    <LoginFormMessage
      id={messageId}
      message={message.text}
      channel={message.channel}
      statusRef={messageRef}
    />
  );
  const panel = (content: ReactNode) => (
    <div
      ref={panelRef}
      tabIndex={-1}
      role="status"
      className="mt-3 flex flex-col gap-2 text-body-sm text-text-primary [overflow-wrap:anywhere]"
    >
      {content}
    </div>
  );

  switch (view.step) {
    case "mailOff":
      // The delivered form: the promise in `description` is not repeated above its own denial.
      return (
        <section className="jp-card">
          {/* Focus lands on the wrapper so the heading is read with the message; role="status" sits
              on the message alone, since nested live regions announce twice. */}
          <div ref={panelRef} tabIndex={-1}>
            {title}
            <p role="status" className="text-body-sm text-text-primary">
              {view.message ?? t("account.errors.emailDeliveryUnavailable")}
            </p>
          </div>
        </section>
      );

    case "addressClosed":
      return (
        <section className="jp-card">
          {title}
          {current}
          <p className="mt-2 text-body-sm text-text-primary">{t("account.changeEmail.description")}</p>
          {panel(<p>{view.message}</p>)}
        </section>
      );

    case "notLoggedIn":
      return (
        <section className="jp-card">
          {title}
          {current}
          {panel(
            <>
              <p>{t("account.reauth.notLoggedIn")}</p>
              <p>
                <Link href="/logga-in?next=/mina-sidor" className={STANDALONE_LINK}>
                  {t("account.reauth.toLogin")}
                </Link>
              </p>
            </>
          )}
        </section>
      );

    case "unknown":
      return (
        <section className="jp-card">
          {title}
          {current}
          {panel(
            <>
              <p>{view.message}</p>
              <p>
                <a href="/mina-sidor" className={STANDALONE_LINK}>
                  {t("account.reload")}
                </a>
              </p>
            </>
          )}
        </section>
      );

    case "codeDead":
      return (
        <section className="jp-card">
          {title}
          {current}
          {panel(<p>{view.message}</p>)}
          <div className="mt-3">
            <Button type="button" className="max-md:h-11" onClick={() => toAddressStep(null)}>
              {t("account.changeEmail.startOver")}
            </Button>
          </div>
        </section>
      );

    case "code":
      return (
        <section className="jp-card">
          {title}
          {current}
          <form onSubmit={onConfirm} noValidate className="mt-3 flex flex-col gap-3">
            <p id={codeStepId} className="text-body-sm text-text-primary [overflow-wrap:anywhere]">
              {t("account.changeEmail.codeStep", { newEmail: view.challenge.address })}
            </p>
            <CodeField
              id={codeId}
              hintId={codeHintId}
              label={t("account.changeEmail.codeLabel")}
              hint={t("account.changeEmail.codeHint")}
              invalid={message?.channel === "field"}
              errorId={messageId}
              leadingDescriptionId={codeStepId}
              inputRef={codeRef}
              value={code}
              onValueChange={setCode}
            />
            {slot}
            <div className="flex flex-wrap items-center gap-x-6 gap-y-3">
              <Button type="submit" disabled={isPending} className="max-md:h-11">
                <PendingLabel
                  pending={isPending}
                  idle={t("account.changeEmail.confirm")}
                  busy={t("account.changeEmail.confirming")}
                />
              </Button>
              <Button
                type="button"
                variant="link"
                size="sm"
                disabled={isPending}
                className={START_OVER_LINK}
                onClick={() => toAddressStep(null)}
              >
                {t("account.changeEmail.startOver")}
              </Button>
            </div>
          </form>
        </section>
      );

    case "address":
      return (
        <section className="jp-card">
          {title}
          {current}
          <p className="mt-2 text-body-sm text-text-primary">{t("account.changeEmail.description")}</p>
          {/* Enter in the field presses "Fortsätt", the form's one submit; the form itself sends
              nothing. */}
          <form
            onSubmit={(event) => event.preventDefault()}
            noValidate
            className="mt-3 flex flex-col gap-3"
          >
            <div className="flex flex-col gap-1.5">
              <Label htmlFor={fieldId}>{t("account.changeEmail.newEmailLabel")}</Label>
              <Input
                ref={fieldRef}
                id={fieldId}
                type="email"
                autoComplete="email"
                spellCheck={false}
                aria-required="true"
                aria-invalid={message?.channel === "field" ? true : undefined}
                aria-describedby={
                  message?.channel === "field" ? `${fieldHintId} ${messageId}` : fieldHintId
                }
                value={input}
                onChange={(event) => setInput(event.target.value)}
              />
              <p id={fieldHintId} className="text-body-sm text-text-primary">
                {t("account.changeEmail.newEmailHint")}
              </p>
            </div>
            {slot}
            <div>
              <ReAuthCodeDialog<{ challengeId: string }>
                trigger={
                  <Button type="submit" variant="secondary" className="max-md:h-11" onClick={onContinue}>
                    {t("account.changeEmail.continue")}
                  </Button>
                }
                title={t("account.changeEmail.title")}
                description={t("account.changeEmail.dialogDescription", { newEmail: pendingAddress })}
                currentEmail={currentEmail}
                confirmLabel={tp("auth.passwordless.code.submit")}
                pendingLabel={tp("auth.passwordless.code.submitting")}
                cancelLabel={t("account.changeEmail.cancel")}
                action={(proof) => requestEmailChangeAction(pendingAddress, proof)}
                onHandOff={onHandOff}
                focusAfterHandOff={focusAfterHandOff}
              />
            </div>
          </form>
        </section>
      );
  }
}
