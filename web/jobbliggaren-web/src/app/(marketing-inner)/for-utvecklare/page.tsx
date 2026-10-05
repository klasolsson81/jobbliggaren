import { InformationPageFrame } from "@/components/information/InformationPageFrame";
import type { Metadata } from "next";
import { InformationLink as Link } from "@/components/information/InformationLink";
import { getTranslations } from "next-intl/server";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("content-legal");
  return {
    title: t("developers.meta.title"),
    description: t("developers.meta.description"),
  };
}

type Section = {
  heading: string;
  paragraphs: string[];
  list?: string[];
};

// Jobbliggarens publika kodförråd (källtillgängligt, PolyForm Noncommercial
// 1.0.0, ADR 0072). Verifierat publikt 2026-06-30. Extern länk öppnas i samma
// flik (myndighetston), samma idiom som /om.
const REPO_URL = "https://github.com/klasolsson81/jobbliggaren";


export default async function ForUtvecklarePage() {
  const t = await getTranslations("content-legal");
  const sections = t.raw("developers.sections") as Section[];

  return (
    <InformationPageFrame title={t("developers.title")} lede={t("developers.lede")} headingId="for-utvecklare-heading">

      <>
        <p className="text-body text-text-primary">{t("developers.intro")}</p>

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
            {t("developers.repoHeading")}
          </h2>
          <p className="mt-3 text-body text-text-primary">
            {t("developers.repoText")}
          </p>
          <p className="mt-2 text-body">
            <a href={REPO_URL} className="underline">
              {t("developers.repoLinkLabel")}
            </a>
          </p>
        </div>

        <div className="mt-12">
          <h2 className="text-body-lg font-semibold text-text-primary">
            {t("developers.relatedHeading")}
          </h2>
          <ul className="mt-3 flex flex-col gap-2 text-body">
            <li>
              <Link href="/om" className="underline">
                {t("developers.relatedAbout")}
              </Link>
            </li>
            <li>
              <Link href="/kontakt" className="underline">
                {t("developers.relatedContact")}
              </Link>
            </li>
          </ul>
        </div>
      </>
    </InformationPageFrame>
  );
}
