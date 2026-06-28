import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("content-legal");
  return {
    title: t("cookies.meta.title"),
    description: t("cookies.meta.description"),
  };
}

type Section = {
  heading: string;
  paragraphs: string[];
  list?: string[];
};

/**
 * Publik innehållssida: Cookiepolicy (#262). Statisk RSC. Innehållet drivs ur
 * `content-legal` (sv = källa, en speglad, paritetstestad), samma mönster som
 * /integritet. Informativ kakpolicy (LEK 2022:482) utan samtyckesbanner, då
 * endast nödvändiga och funktionella förstaparts-kakor används. Civic-utility:
 * en h1, hög-kontrast text, ingen em-dash.
 */
export default async function CookiesPage() {
  const t = await getTranslations("content-legal");
  const sections = t.raw("cookies.sections") as Section[];

  return (
    <main id="main" tabIndex={-1} className="focus:outline-none">
      <section className="jp-pagehero" aria-labelledby="cookies-heading">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            <p className="jp-pagehero__kicker">{t("cookies.kicker")}</p>
            <h1 id="cookies-heading" className="jp-pagehero__title">
              {t("cookies.title")}
            </h1>
          </div>
        </div>
      </section>

      <div className="mx-auto w-full max-w-2xl px-6 py-12">
        <p className="text-body-sm text-text-secondary">{t("cookies.updated")}</p>
        <p className="mt-4 text-body text-text-primary">{t("cookies.intro")}</p>

        <div className="mt-10 flex flex-col gap-8">
          {sections.map((section) => (
            <div key={section.heading}>
              <h2 className="text-body-lg font-semibold text-text-primary">
                {section.heading}
              </h2>
              {section.paragraphs.map((paragraph, i) => (
                <p
                  key={`${section.heading}-p-${i}`}
                  className="mt-3 text-body text-text-primary"
                >
                  {paragraph}
                </p>
              ))}
              {section.list ? (
                <ul className="mt-3 flex list-disc flex-col gap-2 pl-5 text-body text-text-primary">
                  {section.list.map((item, i) => (
                    <li key={`${section.heading}-l-${i}`}>{item}</li>
                  ))}
                </ul>
              ) : null}
            </div>
          ))}
        </div>

        <div className="mt-12">
          <h2 className="text-body-lg font-semibold text-text-primary">
            {t("cookies.relatedHeading")}
          </h2>
          <ul className="mt-3 flex flex-col gap-2 text-body">
            <li>
              <Link href="/integritet" className="underline">
                {t("cookies.relatedPrivacy")}
              </Link>
            </li>
            <li>
              <Link href="/villkor" className="underline">
                {t("cookies.relatedTerms")}
              </Link>
            </li>
          </ul>
        </div>
      </div>
    </main>
  );
}
