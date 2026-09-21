import type { Metadata } from "next";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { ConsentForm } from "@/components/auth/consent-form";
import { FocusHeading } from "@/components/auth/focus-heading";
import { LoginOutcomePanel } from "@/components/auth/login-outcome-panel";
import { readLoginFlow } from "@/lib/auth/login-flow-cookie";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("pages");
  return {
    title: t("auth.passwordless.consent.meta.title"),
    robots: { index: false, follow: false },
  };
}

/**
 * The consent step: reached only by a new address whose code was just proven, and only through
 * the flow cookie's consent phase, which holds the grant and nothing that says whose it is.
 * Not the marketing page `/villkor`, which the checkbox links to.
 */
export default async function LoggaInVillkorPage() {
  const flow = await readLoginFlow();
  if (flow?.phase === "code") redirect("/logga-in/kod");
  if (!flow || flow.phase === "notice") redirect("/logga-in");

  const t = await getTranslations("pages");

  if (flow.phase === "outcome") {
    return (
      <div className="flex flex-col gap-8">
        <FocusHeading>{t("auth.passwordless.consent.title")}</FocusHeading>
        <LoginOutcomePanel result={flow.result} />
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-8">
      <div className="flex flex-col gap-3">
        <FocusHeading>{t("auth.passwordless.consent.title")}</FocusHeading>
        <p className="text-body text-text-primary">
          {t("auth.passwordless.consent.identification")}
        </p>
      </div>
      <ConsentForm />
    </div>
  );
}
