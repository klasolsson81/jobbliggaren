import Link from "next/link";
import { getTranslations } from "next-intl/server";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("publicjob");
  return { title: t("busy.title"), robots: { index: false, follow: false } };
}

/**
 * Svaret när annonsen inte kan visas just nu (ADR 0160 D9.5): `proxy.ts` skriver om hit med status 503 och
 * `Retry-After`, för både budgeten (429) och ett avbrott. Orsaken är oftast okänd för oss, så sidan påstår ingen.
 */
export default async function PublicJobBusyPage() {
  const t = await getTranslations("publicjob");
  const tFallback = await getTranslations("fallback");

  return (
    <main id="main" tabIndex={-1} className="jp-container jp-page flex flex-col gap-4 focus:outline-none">
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
