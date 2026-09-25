import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { EmailEntryForm } from "@/components/auth/email-entry-form";
import { LoginFlowNotice } from "@/components/auth/login-flow-notice";
import { ProviderButtons } from "@/components/auth/provider-buttons";
import { getExternalLoginProviders } from "@/lib/api/oauth-providers";
import { readLoginFlow } from "@/lib/auth/login-flow-cookie";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("pages");
  return { title: t("auth.passwordless.entry.meta.title") };
}

interface PageProps {
  searchParams: Promise<{ next?: string | string[] }>;
}

/** The persistence line's id, which each active provider row is described by (security-auditor S1). */
const PERSISTENCE_ID = "login-persistence";

/**
 * The one auth page (ADR 0142): an address that has an account logs in, one that has none creates
 * one, and the page cannot tell which and must not.
 *
 * It reads the flow cookie for ONE thing, a notice. It never resumes a login from the cookie and
 * never redirects on it: "Byt e-postadress" lands here, and a page that bounced back to the code
 * step would make that button a loop. Nor does it render an outcome phase, which a Server
 * Component could not clear, so a visitor coming back here would meet a stale panel.
 *
 * Two orders (ADR 0142 "Page form"). With no live provider, the address comes first and the inactive
 * rows last. With one, the providers come first under the persistence line, which a provider's
 * login gives effect without passing a step that shows it; then "Eller fortsätt med e-post", and
 * the address with the lede that belongs to it (design-reviewer M5, M6).
 */
export default async function LoggaInPage({ searchParams }: PageProps) {
  const t = await getTranslations("pages");
  const { next } = await searchParams;
  // A repeated `next` arrives as an array; the action and the start each validate what they are given.
  const nextValue = (Array.isArray(next) ? next[0] : next) ?? "";
  const [flow, providers] = await Promise.all([readLoginFlow(), getExternalLoginProviders()]);
  const notice =
    flow?.phase === "notice" ? <LoginFlowNotice notice={flow.notice} provider={flow.provider} /> : null;

  if (providers.length === 0) {
    return (
      <div className="flex flex-col gap-8">
        <div className="flex flex-col gap-3">
          <h1 className="text-h1 font-bold text-heading-1">{t("auth.passwordless.entry.title")}</h1>
          <p className="text-body text-text-primary">{t("auth.passwordless.entry.lede")}</p>
        </div>

        {notice}

        <EmailEntryForm next={nextValue} />

        <div className="flex flex-col gap-4 border-t border-border pt-8">
          <h2 className="text-body font-bold text-heading-1">
            {t("auth.passwordless.entry.providersHeading")}
          </h2>
          <ProviderButtons />
        </div>
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-8">
      <h1 className="text-h1 font-bold text-heading-1">{t("auth.passwordless.entry.title")}</h1>

      {notice}

      <div className="flex flex-col gap-4">
        <p id={PERSISTENCE_ID} className="text-body-sm text-text-primary">
          {t("auth.passwordless.persistence")}
        </p>
        <ProviderButtons active={providers} next={nextValue} describedBy={PERSISTENCE_ID} />
      </div>

      <div className="flex items-center gap-3">
        <span aria-hidden="true" className="h-px flex-1 bg-border" />
        <p className="text-body text-text-primary">{t("auth.passwordless.entry.orEmail")}</p>
        <span aria-hidden="true" className="h-px flex-1 bg-border" />
      </div>

      <div className="flex flex-col gap-3">
        <p className="text-body text-text-primary">{t("auth.passwordless.entry.lede")}</p>
        <EmailEntryForm next={nextValue} />
      </div>
    </div>
  );
}
