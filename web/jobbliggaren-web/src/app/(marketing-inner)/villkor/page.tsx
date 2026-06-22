import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("landing");
  return {
    title: t("meta.termsTitle"),
    description: t("meta.termsDescription"),
  };
}

/**
 * Placeholder för användarvillkor. Versionerad policy-text är öppen fråga i
 * BUILD.md §20 — levereras av Klas innan första prod-deploy med riktig
 * användarbas.
 */
export default async function VillkorPage() {
  const t = await getTranslations("landing");
  return (
    <>
      <header className="jp-pagehero">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            <p className="jp-pagehero__kicker">{t("terms.kicker")}</p>
            <h1 className="jp-pagehero__title">{t("terms.title")}</h1>
          </div>
        </div>
      </header>

      <main className="mx-auto w-full max-w-2xl px-6 py-12">
        <section className="flex flex-col gap-4">
          <p className="text-body text-text-primary">{t("terms.intro")}</p>
          <p className="text-body text-text-secondary">{t("terms.duringBeta")}</p>
          <ul className="flex flex-col gap-2 text-body text-text-secondary">
            <li>{t("terms.asIs")}</li>
            <li>{t("terms.dataRetention")}</li>
            <li>{t("terms.dataDeletion")}</li>
          </ul>
        </section>
      </main>
    </>
  );
}
