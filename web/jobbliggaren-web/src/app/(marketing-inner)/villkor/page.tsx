import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("content-legal");
  return {
    title: t("terms.meta.title"),
    description: t("terms.meta.description"),
  };
}

type Section = {
  heading: string;
  paragraphs: string[];
  list?: string[];
};

/**
 * Publik innehållssida: Användarvillkor (#262). Statisk RSC. Innehållet drivs
 * ur `content-legal` (sv = källa, en speglad, paritetstestad), samma mönster som
 * /integritet. Civic-utility: en h1, hög-kontrast text, ingen em-dash.
 */
export default async function VillkorPage() {
  const t = await getTranslations("content-legal");
  const sections = t.raw("terms.sections") as Section[];

  return (
    <main id="main" tabIndex={-1} className="focus:outline-none">
      <section className="jp-pagehero" aria-labelledby="villkor-heading">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            <p className="jp-pagehero__kicker">{t("terms.kicker")}</p>
            <h1 id="villkor-heading" className="jp-pagehero__title">
              {t("terms.title")}
            </h1>
          </div>
        </div>
      </section>

      <div className="mx-auto w-full max-w-2xl px-6 py-12">
        <p className="text-body-sm text-text-secondary">{t("terms.updated")}</p>
        <p className="mt-4 text-body text-text-primary">{t("terms.intro")}</p>

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
            {t("terms.relatedHeading")}
          </h2>
          <ul className="mt-3 flex flex-col gap-2 text-body">
            <li>
              <Link href="/integritet" className="underline">
                {t("terms.relatedPrivacy")}
              </Link>
            </li>
            <li>
              <Link href="/cookies" className="underline">
                {t("terms.relatedCookies")}
              </Link>
            </li>
          </ul>
        </div>
      </div>
    </main>
  );
}
