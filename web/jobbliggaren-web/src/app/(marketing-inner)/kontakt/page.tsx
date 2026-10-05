import { InformationPageFrame } from "@/components/information/InformationPageFrame";
import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("content-legal");
  return {
    title: t("contact.meta.title"),
    description: t("contact.meta.description"),
  };
}


export default async function KontaktPage() {
  const t = await getTranslations("content-legal");
  const aboutList = t.raw("contact.aboutList") as string[];
  const email = t("contact.email");

  return (
    <InformationPageFrame title={t("contact.title")} lede={t("contact.lede")} headingId="kontakt-heading">

      <>
        <p className="text-body text-text-primary">{t("contact.intro")}</p>

        <p className="mt-4 text-body text-text-primary">
          {t("contact.emailLabel")}{" "}
          <a href={`mailto:${email}`} className="underline">
            {email}
          </a>
        </p>

        <p className="mt-6 text-body text-text-primary">
          {t("contact.aboutListIntro")}
        </p>
        <ul className="mt-3 flex list-disc flex-col gap-2 pl-5 text-body text-text-primary">
          {aboutList.map((item, i) => (
            <li key={`contact-${i}`}>{item}</li>
          ))}
        </ul>
      </>
    </InformationPageFrame>
  );
}
