import Link from "next/link";
import { getTranslations } from "next-intl/server";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("publicjob");
  return { title: t("busy.title"), robots: { index: false, follow: false } };
}

/**
 * The answer when the ad cannot be shown right now (ADR 0160 D9.5): `proxy.ts` rewrites here with status 503 and
 * `Retry-After`, for the budget (429) and for an outage alike. The cause is usually unknown to us, so the page names none.
 */
export default async function PublicJobBusyPage() {
  const t = await getTranslations("publicjob");
  const tFallback = await getTranslations("fallback");

  return (
    <main
      id="main"
      tabIndex={-1}
      className="jp-container jp-page flex min-h-[60vh] flex-col justify-center gap-4 focus:outline-none"
    >
      <h1 className="jp-h1">{t("busy.title")}</h1>
      <p className="jp-lede">{t("busy.body")}</p>
      <div>
        <Link href="/" className="jp-btn jp-btn--secondary">
          {tFallback("notFound.toStart")}
        </Link>
      </div>
    </main>
  );
}
