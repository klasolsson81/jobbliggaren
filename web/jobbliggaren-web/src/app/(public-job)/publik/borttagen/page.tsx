import Link from "next/link";
import { getTranslations } from "next-intl/server";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("publicjob");
  return { title: t("gone.title"), robots: { index: false, follow: false } };
}

/**
 * The answer for an ended or an erased ad (ADR 0160 D3, D9.7): `proxy.ts` rewrites here with status 410. The same page,
 * word for word, in both cases: a different text, a title, a company or a date would show that someone asked for
 * erasure (Art. 17). So the page has no id in scope and reads nothing.
 */
export default async function PublicJobGonePage() {
  const t = await getTranslations("publicjob");
  const tFallback = await getTranslations("fallback");

  return (
    <main
      id="main"
      tabIndex={-1}
      className="jp-container jp-page flex min-h-[60vh] flex-col justify-center gap-4 focus:outline-none"
    >
      <h1 className="jp-h1">{t("gone.title")}</h1>
      <p className="jp-lede">{t("gone.body")}</p>
      <div>
        <Link href="/" className="jp-btn jp-btn--secondary">
          {tFallback("notFound.toStart")}
        </Link>
      </div>
    </main>
  );
}
