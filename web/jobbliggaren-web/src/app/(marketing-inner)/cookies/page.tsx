import { InformationPageFrame } from "@/components/information/InformationPageFrame";
import { legalSectionId } from "@/components/information/section-ids";
import type { Metadata } from "next";
import { InformationLink as Link } from "@/components/information/InformationLink";
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


export default async function CookiesPage() {
  const t = await getTranslations("content-legal");
  const sections = t.raw("cookies.sections") as Section[];

  return (
    <InformationPageFrame title={t("cookies.title")} lede={t("cookies.updated")} headingId="cookies-heading" sections={sections.map((section, index) => ({ id: legalSectionId("cookies", index), label: section.heading }))}>

      <>
        <p className="text-body text-text-primary">{t("cookies.intro")}</p>

        <div className="mt-10 flex flex-col gap-8">
          {sections.map((section, index) => (
            <div key={section.heading}>
              <h2 id={legalSectionId("cookies", index)} tabIndex={-1} className="text-body-lg font-semibold text-text-primary">
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
      </>
    </InformationPageFrame>
  );
}
