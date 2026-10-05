import { InformationPageFrame } from "@/components/information/InformationPageFrame";
import type { Metadata } from "next";
import { InformationLink as Link } from "@/components/information/InformationLink";
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


export default async function KontaktpersonIAnnonsPage() {
  const t = await getTranslations("content-legal");
  const sections = t.raw("recruiterNotice.sections") as Section[];

  return (
    <InformationPageFrame title={t("recruiterNotice.title")} lede={t("recruiterNotice.updated")} headingId="kontaktperson-heading">

      <>
        <p className="text-body text-text-primary">
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
      </>
    </InformationPageFrame>
  );
}
