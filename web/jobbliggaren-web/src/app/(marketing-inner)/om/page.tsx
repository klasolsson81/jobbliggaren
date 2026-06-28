import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("content-legal");
  return {
    title: t("about.meta.title"),
    description: t("about.meta.description"),
  };
}

/**
 * Publik innehållssida: Om Jobbliggaren (#262). Statisk RSC. Återbrukar
 * (marketing-inner)-mönstret (delad SiteHeader/SiteFooter via layouten; eget
 * `<main id="main">` som skip-mål per #284). Innehållet drivs ur `content-legal`
 * (sv = källa, en speglad, paritetstestad). Civic-utility: en h1, hög-kontrast
 * text, ingen em-dash, inget utropstecken. Externa länkar (klasolsson.se,
 * kalaskoll.se) öppnas i samma flik (myndighetston).
 */
export default async function OmPage() {
  const t = await getTranslations("content-legal");
  const whyParagraphs = t.raw("about.whyParagraphs") as string[];
  const values = t.raw("about.values") as string[];

  return (
    <main id="main" tabIndex={-1} className="focus:outline-none">
      <section className="jp-pagehero" aria-labelledby="om-heading">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            <p className="jp-pagehero__kicker">{t("about.kicker")}</p>
            <h1 id="om-heading" className="jp-pagehero__title">
              {t("about.title")}
            </h1>
          </div>
        </div>
      </section>

      <div className="mx-auto w-full max-w-2xl px-6 py-12">
        <p className="text-body text-text-primary">{t("about.intro")}</p>

        <h2 className="mt-10 text-body-lg font-semibold text-text-primary">
          {t("about.whyHeading")}
        </h2>
        {whyParagraphs.map((paragraph, i) => (
          <p key={`why-${i}`} className="mt-3 text-body text-text-primary">
            {paragraph}
          </p>
        ))}

        <h2 className="mt-10 text-body-lg font-semibold text-text-primary">
          {t("about.valuesHeading")}
        </h2>
        <p className="mt-3 text-body text-text-primary">{t("about.valuesIntro")}</p>
        <ul className="mt-3 flex list-disc flex-col gap-2 pl-5 text-body text-text-primary">
          {values.map((value, i) => (
            <li key={`value-${i}`}>{value}</li>
          ))}
        </ul>

        <h2 className="mt-10 text-body-lg font-semibold text-text-primary">
          {t("about.moreHeading")}
        </h2>
        <p className="mt-3 text-body text-text-primary">{t("about.moreText")}</p>
        <ul className="mt-3 flex flex-col gap-2 text-body">
          <li>
            <a href="https://klasolsson.se" className="underline">
              {t("about.linkSite")}
            </a>
          </li>
          <li>
            <a href="https://kalaskoll.se" className="underline">
              {t("about.linkKalaskoll")}
            </a>
          </li>
        </ul>

        <h2 className="mt-10 text-body-lg font-semibold text-text-primary">
          {t("about.contactHeading")}
        </h2>
        <p className="mt-3 text-body text-text-primary">{t("about.contactText")}</p>
        <p className="mt-2 text-body">
          <Link href="/kontakt" className="underline">
            {t("about.contactLink")}
          </Link>
        </p>
      </div>
    </main>
  );
}
