import { InformationPageFrame } from "@/components/information/InformationPageFrame";
import { legalSectionId } from "@/components/information/section-ids";
import type { Metadata } from "next";
import { InformationLink as Link } from "@/components/information/InformationLink";
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


export default async function IntegritetPage({ searchParams }: { searchParams?: Promise<{ context?: string }> } = {}) {
  const t = await getTranslations("content-legal");
  const navigation = await getTranslations("information");
  const context = (await searchParams)?.context;
  const sections = t.raw("privacy.sections") as Section[];

  return (
    <InformationPageFrame title={t("privacy.title")} lede={t("privacy.updated")} headingId="integritet-heading" notice={context === "cv-upload" ? navigation("cvTab") : undefined} sections={sections.map((section, index) => ({ id: legalSectionId("integritet", index), label: section.heading }))}>

      <>
        <p className="text-body text-text-primary">{t("privacy.intro")}</p>

        <div className="mt-10 flex flex-col gap-8">
          {sections.map((section, index) => (
            <div key={section.heading}>
              <h2 id={legalSectionId("integritet", index)} tabIndex={-1} className="text-body-lg font-semibold text-text-primary">
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
            <li>
              <Link href="/kontaktperson-i-annons" className="underline">
                {t("privacy.relatedRecruiterNotice")}
              </Link>
            </li>
          </ul>
        </div>
      </>
    </InformationPageFrame>
  );
}
