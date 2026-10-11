import Link from "next/link";
import { getTranslations } from "next-intl/server";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("publicjob");
  return { title: t("gone.title"), robots: { index: false, follow: false } };
}

/**
 * Svaret på en avslutad eller raderad annons (ADR 0160 D3, D9.7): `proxy.ts` skriver om hit med status 410.
 * Samma sida, ord för ord, i båda fallen — en annan text, en titel, ett företag eller ett datum skulle visa att
 * någon bett om radering (Art. 17), så sidan har inget id i sitt scope och läser inget.
 */
export default async function PublicJobGonePage() {
  const t = await getTranslations("publicjob");
  const tFallback = await getTranslations("fallback");

  return (
    <main id="main" tabIndex={-1} className="jp-container jp-page flex flex-col gap-4 focus:outline-none">
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
