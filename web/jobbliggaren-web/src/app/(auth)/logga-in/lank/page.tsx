import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { LinkLandingForm, UnusableLink } from "@/components/auth/link-landing-form";
import { linkTokenInputSchema } from "@/lib/auth/challenge-schemas";
import { getSessionId } from "@/lib/auth/session";
import { LOGIN_LINK_REFERRER_POLICY } from "@/lib/security/security-headers";

// Never statically rendered or cached, under any future config: the URL carries a login token.
export const dynamic = "force-dynamic";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("pages");
  return {
    title: t("auth.passwordless.link.meta.title"),
    robots: { index: false, follow: false },
    // `same-origin`, not the other token pages' `no-referrer`: see `LOGIN_LINK_REFERRER_POLICY`.
    referrer: LOGIN_LINK_REFERRER_POLICY,
  };
}

interface PageProps {
  searchParams: Promise<{ token?: string | string[] }>;
}

/** A manipulated URL can repeat the parameter; only the first value is read, and nothing else. */
function single(value: string | string[] | undefined): string {
  return (Array.isArray(value) ? value[0] : value)?.trim() ?? "";
}

/**
 * Where the login link in the mail lands. It reads `token` and ignores every other query
 * parameter, and never the flow cookie: the click comes from a mail client, cross-site, and a
 * Strict cookie is not sent with it. The GET consumes nothing and reads no record.
 *
 * A session cookie already in this browser switches the page to the arm that says what continuing
 * does and asks for a choice (`LinkLandingForm`). Its presence is all that is read: a stale cookie
 * only shows the question once more than needed.
 */
export default async function LoggaInLankPage({ searchParams }: PageProps) {
  const t = await getTranslations("pages");
  const token = linkTokenInputSchema.safeParse(single((await searchParams).token));
  const alreadyLoggedIn = (await getSessionId()) !== null;

  return (
    <div className="flex flex-col gap-8">
      <h1 className="text-h1 font-bold text-heading-1">
        {alreadyLoggedIn && token.success
          ? t("auth.passwordless.link.alreadyLoggedIn.title")
          : t("auth.passwordless.link.title")}
      </h1>
      {token.success ? (
        <LinkLandingForm token={token.data} alreadyLoggedIn={alreadyLoggedIn} />
      ) : (
        <UnusableLink />
      )}
    </div>
  );
}
