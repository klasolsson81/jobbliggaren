import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { ConfirmEmailChange } from "@/components/auth/confirm-email-change";

// #679 — PUBLIC confirm-email-change landing. Lives OUTSIDE (app)/ under (auth)/ on
// purpose: the link is opened from the NEW inbox and the visitor may be logged out,
// so it must stay reachable without a session. Because it is not an (app)/ segment it
// is not in PROTECTED_PREFIXES (protected-routes.test.ts derives that set from the
// (app)/ directory), so the proxy never redirects it to /logga-in.

// The URL carries a single-use confirmation token; keep it out of search indexes as
// defense-in-depth (the page is only ever reached from an emailed link).
export const metadata: Metadata = {
  robots: { index: false, follow: false },
};

interface PageProps {
  // Next.js 16 App Router: searchParams is a Promise (see
  // node_modules/next/dist/docs/.../file-conventions/page.md#searchparams-optional).
  searchParams: Promise<{
    uid?: string | string[];
    email?: string | string[];
    token?: string | string[];
  }>;
}

// A manipulated URL can repeat a param → string[]; take the first value. Next.js has
// already percent-decoded the values (searchParams is a plain decoded object).
function single(value: string | string[] | undefined): string {
  return (Array.isArray(value) ? value[0] : value)?.trim() ?? "";
}

export default async function BekraftaEpostPage({ searchParams }: PageProps) {
  const t = await getTranslations("pages");
  const params = await searchParams;
  const uid = single(params.uid);
  const email = single(params.email);
  const token = single(params.token);

  // Missing params → a clear "invalid link" state WITHOUT POSTing. A garbled or
  // absent link must never trigger the confirm request.
  if (!uid || !email || !token) {
    return (
      <div className="flex flex-col gap-6">
        <div className="flex flex-col gap-1">
          <h1 className="text-h1 font-bold text-heading-1">
            {t("auth.confirmEmailChange.invalidTitle")}
          </h1>
          <p className="text-body text-text-primary">
            {t("auth.confirmEmailChange.invalidBody")}
          </p>
        </div>
        <div>
          <Link
            href="/logga-in"
            className="text-brand-700 hover:text-[var(--jp-accent-600)] underline underline-offset-2"
          >
            {t("auth.confirmEmailChange.loginLink")}
          </Link>
        </div>
      </div>
    );
  }

  // The token triple is present. Hand it to the client island, which fires the PUBLIC
  // confirm POST ONLY on an explicit button click (never on load — mail scanners and
  // prefetchers GET the link and would otherwise auto-consume a single-use token).
  return <ConfirmEmailChange uid={uid} email={email} token={token} />;
}
