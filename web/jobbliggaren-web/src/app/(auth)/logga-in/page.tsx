import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { EmailEntryForm } from "@/components/auth/email-entry-form";
import { LoginFlowNotice } from "@/components/auth/login-flow-notice";
import { ProviderButtons } from "@/components/auth/provider-buttons";
import { readLoginFlow } from "@/lib/auth/login-flow-cookie";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("pages");
  return { title: t("auth.passwordless.entry.meta.title") };
}

interface PageProps {
  searchParams: Promise<{ next?: string | string[] }>;
}

/**
 * The one auth page (ADR 0142): an address that has an account logs in, one that has none creates
 * one, and the page cannot tell which and must not.
 *
 * It reads the flow cookie for ONE thing, a notice. It never resumes a login from the cookie and
 * never redirects on it: "Byt e-postadress" lands here, and a page that bounced back to the code
 * step would make that button a loop. Nor does it render an outcome phase, which a Server
 * Component could not clear, so a visitor coming back here would meet a stale panel.
 *
 * The provider list is empty until part 6a, so the order is the "no provider live" one: the
 * address first, the inactive rows last. `GET /auth/oauth/providers` does not exist yet.
 */
export default async function LoggaInPage({ searchParams }: PageProps) {
  const t = await getTranslations("pages");
  const { next } = await searchParams;
  const flow = await readLoginFlow();

  return (
    <div className="flex flex-col gap-8">
      <div className="flex flex-col gap-3">
        <h1 className="text-h1 font-bold text-heading-1">
          {t("auth.passwordless.entry.title")}
        </h1>
        <p className="text-body text-text-primary">{t("auth.passwordless.entry.lede")}</p>
      </div>

      {flow?.phase === "notice" && <LoginFlowNotice notice={flow.notice} />}

      {/* A repeated `next` arrives as an array; the action validates whatever it is given. */}
      <EmailEntryForm next={(Array.isArray(next) ? next[0] : next) ?? ""} />

      <div className="flex flex-col gap-4 border-t border-border pt-8">
        <h2 className="text-body font-bold text-heading-1">
          {t("auth.passwordless.entry.providersHeading")}
        </h2>
        <ProviderButtons />
      </div>
    </div>
  );
}
