import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("content-legal");
  return {
    title: t("privacy.meta.title"),
    description: t("privacy.meta.description"),
  };
}

type Section = {
  heading: string;
  paragraphs: string[];
  list?: string[];
};

/**
 * Publik innehållssida: Integritetspolicy (#263). Statisk RSC, ingen
 * klient-interaktivitet. Återbrukar (marketing-inner)-mönstret (delad
 * SiteHeader/SiteFooter via layouten; eget `<main id="main">` som skip-mål per
 * #284). Innehållet drivs ur `content-legal`-katalogen (sv = källa, en speglad,
 * paritetstestad). Civic-utility: en h1, hög-kontrast text (ingen grå brödtext),
 * ingen em-dash, inget utropstecken.
 */
export default async function IntegritetPage() {
  const t = await getTranslations("content-legal");
  const sections = t.raw("privacy.sections") as Section[];

  return (
    <main id="main" tabIndex={-1} className="focus:outline-none">
      <section className="jp-pagehero" aria-labelledby="integritet-heading">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            <h1 id="integritet-heading" className="jp-pagehero__title">
              {t("privacy.title")}
            </h1>
          </div>
        </div>
      </section>

      <div className="mx-auto w-full max-w-2xl px-6 py-12">
        <p className="text-body-sm text-text-secondary">{t("privacy.updated")}</p>
        <p className="mt-4 text-body text-text-primary">{t("privacy.intro")}</p>

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
            {t("privacy.relatedHeading")}
          </h2>
          <ul className="mt-3 flex flex-col gap-2 text-body">
            <li>
              <Link href="/villkor" className="underline">
                {t("privacy.relatedTerms")}
              </Link>
            </li>
            <li>
              <Link href="/cookies" className="underline">
                {t("privacy.relatedCookies")}
              </Link>
            </li>
          </ul>
        </div>
      </div>
    </main>
  );
}
