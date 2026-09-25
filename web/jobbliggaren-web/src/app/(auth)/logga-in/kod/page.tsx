import type { Metadata } from "next";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { ChangeEmailButton } from "@/components/auth/change-email-button";
import { CodeForm } from "@/components/auth/code-form";
import { DeadCodePanel } from "@/components/auth/dead-code-panel";
import { FocusHeading } from "@/components/auth/focus-heading";
import { LoginOutcomePanel } from "@/components/auth/login-outcome-panel";
import { ResendCodeButton } from "@/components/auth/resend-code-button";
import { resendCooldownRemaining } from "@/lib/auth/login-flow";
import { nowEpochSeconds, readLoginFlow } from "@/lib/auth/login-flow-cookie";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("pages");
  const flow = await readLoginFlow();
  return {
    title:
      flow?.phase === "outcome" && flow.via
        ? t("auth.passwordless.external.title", {
            provider: t(`auth.passwordless.external.providerNames.${flow.via}`),
          })
        : t("auth.passwordless.code.meta.title"),
    robots: { index: false, follow: false },
  };
}

/**
 * The code step. Everything it shows comes from the flow cookie, so it is the same after a reload
 * and without JavaScript: the form, a dead code, or the outcome of a verify.
 *
 * It may redirect on a phase it does not own ONLY because no action returns a state after writing
 * the cookie (`challenge-actions.ts`): every write ends in a redirect, so there is never a panel
 * for this check to swallow.
 *
 * The resting copy says what the user should do and never what the system did. The mail goes to
 * the account's own stored spelling, not necessarily the one typed, and two branches write a
 * challenge and send nothing; the uniform 202 means the page cannot know which happened. The typed
 * address appears as its own statement, away from any sentence about a mail, so a typo is
 * discoverable without claiming a recipient.
 */
export default async function LoggaInKodPage() {
  const flow = await readLoginFlow();
  if (flow?.phase === "consent") redirect("/logga-in/villkor");
  if (!flow || flow.phase === "notice") redirect("/logga-in");

  const t = await getTranslations("pages");

  if (flow.phase === "outcome") {
    return (
      <div className="flex flex-col gap-8">
        <FocusHeading>
          {flow.via
            ? t("auth.passwordless.external.title", {
                provider: t(`auth.passwordless.external.providerNames.${flow.via}`),
              })
            : t("auth.passwordless.code.title")}
        </FocusHeading>
        <LoginOutcomePanel result={flow.result} />
      </div>
    );
  }

  // The three children keep their positions whichever arm renders, so `ResendCodeButton` keeps
  // its state (its receipt) across the re-render that brings the field back after a dead code.
  return (
    <div className="flex flex-col gap-8">
      <FocusHeading>{t("auth.passwordless.code.title")}</FocusHeading>

      {flow.dead ? (
        <DeadCodePanel reason={flow.dead} />
      ) : (
        <div className="flex flex-col gap-5">
          <p className="text-body text-text-primary">{t("auth.passwordless.code.resting")}</p>
          <CodeForm />
        </div>
      )}

      <div className="flex flex-col gap-4 border-t border-border pt-8">
        <p className="text-body-sm text-text-primary [overflow-wrap:anywhere]">
          {t("auth.passwordless.code.youEntered", { email: flow.email })}
        </p>
        <ResendCodeButton
          sentAt={flow.sentAt}
          initialCooldownSeconds={resendCooldownRemaining(flow, nowEpochSeconds())}
          primary={flow.dead !== undefined}
        />
        <ChangeEmailButton label={t("auth.passwordless.code.changeEmail")} />
      </div>
    </div>
  );
}
