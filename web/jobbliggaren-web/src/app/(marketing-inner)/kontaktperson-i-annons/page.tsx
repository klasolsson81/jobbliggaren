import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("content-legal");
  return {
    title: t("recruiterNotice.meta.title"),
    description: t("recruiterNotice.meta.description"),
  };
}

type Section = {
  heading: string;
  paragraphs: string[];
  list?: string[];
};

/**
 * Publik innehållssida: Art. 14(5)(b)-notis till kontaktpersoner i annonser
 * (#842 Tier A, ADR 0106). Vi håller ~tiotusentals rekryterares kontaktuppgifter
 * hämtade från Arbetsförmedlingen, inte från dem själva — informationsplikten
 * fullgörs genom att göra informationen offentligt tillgänglig (Art. 14(5)(b);
 * ett massutskick vore en ny behandling av just de uppgifter vi minimerar).
 * Notisen är villkoret för undantaget, inte dekoration. Länkas från
 * annonsdetaljen och integritetspolicyn. Samma statiska RSC-mönster som
 * /integritet (#263): innehåll ur `content-legal`, en h1, hög-kontrast text.
 */
export default async function KontaktpersonIAnnonsPage() {
  const t = await getTranslations("content-legal");
  const sections = t.raw("recruiterNotice.sections") as Section[];

  return (
    <main id="main" tabIndex={-1} className="focus:outline-none">
      <section className="jp-pagehero" aria-labelledby="kontaktperson-heading">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            <h1 id="kontaktperson-heading" className="jp-pagehero__title">
              {t("recruiterNotice.title")}
            </h1>
          </div>
        </div>
      </section>

      <div className="mx-auto w-full max-w-2xl px-6 py-12">
        <p className="text-body-sm text-text-secondary">
          {t("recruiterNotice.updated")}
        </p>
        <p className="mt-4 text-body text-text-primary">
          {t("recruiterNotice.intro")}
        </p>

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
            {t("recruiterNotice.relatedHeading")}
          </h2>
          <ul className="mt-3 flex flex-col gap-2 text-body">
            <li>
              <Link href="/integritet" className="underline">
                {t("recruiterNotice.relatedPrivacy")}
              </Link>
            </li>
          </ul>
        </div>
      </div>
    </main>
  );
}
