import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("landing");
  return {
    title: t("meta.cookiesTitle"),
    description: t("meta.cookiesDescription"),
  };
}

/**
 * Placeholder för cookie-policy. Versionerad policy-text är öppen fråga i
 * BUILD.md §20 — levereras av Klas innan första prod-deploy.
 */
export default async function CookiesPage() {
  const t = await getTranslations("landing");
  return (
    <>
      <header className="jp-pagehero">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            <p className="jp-pagehero__kicker">{t("cookies.kicker")}</p>
            <h1 className="jp-pagehero__title">{t("cookies.title")}</h1>
          </div>
        </div>
      </header>

      <main className="mx-auto w-full max-w-2xl px-6 py-12">
        <section className="flex flex-col gap-4">
          <p className="text-body text-text-primary">{t("cookies.intro")}</p>
          <p className="text-body text-text-secondary">
            {t("cookies.meansLabel")}
          </p>
          <ul className="flex flex-col gap-2 text-body text-text-secondary">
            <li>
              <span className="font-medium">{t("cookies.sessionTerm")}</span>
              {t("cookies.sessionBody")}
            </li>
            <li>
              <span className="font-medium">{t("cookies.csrfTerm")}</span>
              {t("cookies.csrfBody")}
            </li>
            <li>{t("cookies.noTracking")}</li>
          </ul>

          <p className="text-body text-text-secondary pt-2">
            {t("cookies.fullPolicyNote")}
          </p>
        </section>
      </main>
    </>
  );
}
