import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { ResetPassword } from "@/components/auth/reset-password";

// #1171 — PUBLIC password-reset landing. Lives OUTSIDE (app)/ under (auth)/ on purpose: the link is
// opened from the account's inbox with no session, so it must stay reachable logged out. Not an (app)/
// segment, so it is not in PROTECTED_PREFIXES and the proxy never redirects it to /logga-in.

// The URL carries a single-use credential that can change the account's password — stronger than the
// confirmation links, so the same noindex applies with more reason. Referrer leakage is already closed
// separately: buildSecurityHeaders sets Referrer-Policy: strict-origin-when-cross-origin, so a
// cross-origin request sends the origin without the path or query.
export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("pages");

  return {
    title: t("auth.resetPassword.meta.title"),
    robots: { index: false, follow: false },
    // The URL's query carries the token, and Referrer-Policy: strict-origin-when-cross-origin strips
    // path+query only CROSS-origin — a same-origin navigation would send the whole URL, token included.
    // The edge CAN persist it: no access log is configured, but Caddy's default http.log.error logger
    // emits the request line with unredacted Referer on 5xx even with no `log` directive (measured
    // 2026-08-11, PR #1313) — "no-referrer" makes the absence of the leak a property of this page
    // rather than of the edge config.
    referrer: "no-referrer",
  };
}

interface PageProps {
  // Next.js 16 App Router: searchParams is a Promise.
  searchParams: Promise<{
    uid?: string | string[];
    token?: string | string[];
  }>;
}

// A manipulated URL can repeat a param → string[]; take the first value. Next.js has already
// percent-decoded the values (searchParams is a plain decoded object).
function single(value: string | string[] | undefined): string {
  return (Array.isArray(value) ? value[0] : value)?.trim() ?? "";
}

export default async function AterstallLosenordPage({ searchParams }: PageProps) {
  const t = await getTranslations("pages");
  const params = await searchParams;
  const uid = single(params.uid);
  const token = single(params.token);

  // Missing params → a clear "invalid link" state WITHOUT POSTing. A garbled or absent link must never
  // reach the reset endpoint.
  if (!uid || !token) {
    return (
      <div className="flex flex-col gap-6">
        <div className="flex flex-col gap-1">
          <h1 className="text-h1 font-bold text-heading-1">
            {t("auth.resetPassword.invalidTitle")}
          </h1>
          <p className="text-body text-text-primary">
            {t("auth.resetPassword.invalidBody")}
          </p>
        </div>
        <div>
          <Link
            href="/glomt-losenord"
            className="text-brand-700 underline underline-offset-2"
          >
            {t("auth.forgotPassword.requestNewLink")}
          </Link>
        </div>
      </div>
    );
  }

  // The (uid, token) pair is present. Hand it to the client island, which POSTs only on an explicit
  // submit — never on load, because mail scanners and prefetchers GET the link and a reset that fired
  // on GET would spend the token before the user ever saw the form.
  return <ResetPassword uid={uid} token={token} />;
}
