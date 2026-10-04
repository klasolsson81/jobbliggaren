import { InformationPageFrame } from "@/components/information/InformationPageFrame";
import type { Metadata } from "next";
import { InformationLink as Link } from "@/components/information/InformationLink";
import { getTranslations } from "next-intl/server";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("content-tips");
  return {
    title: t("meta.title"),
    description: t("meta.description"),
  };
}

// Fast sektion-ordning för Tips. Faktiska råd, inget säljspråk, säg varje sak
// en gång (K1).
const TIP_KEYS = [
  "cv",
  "ansokan",
  "uppfoljning",
  "intervju",
  "deadlines",
] as const;


export default async function TipsPage() {
  const t = await getTranslations("content-tips");
  const sections = TIP_KEYS.map((key) => ({
    title: t(`sections.${key}.title`),
    body: t(`sections.${key}.body`),
  }));

  return (
    <InformationPageFrame title={t("title")} lede={t("lede")} headingId="tips-heading">

      <>
        <div className="flex flex-col gap-8">
          {sections.map((section, index) => (
            <section key={TIP_KEYS[index]}>
              <h2 className="text-body font-semibold text-text-primary">
                {section.title}
              </h2>
              <p className="mt-2 text-body text-text-primary">{section.body}</p>
            </section>
          ))}
        </div>

        <div className="mt-10 flex flex-wrap gap-4">
          <Link
            href="/jobb"
            className="font-medium text-text-primary underline underline-offset-4 hover:no-underline"
          >
            {t("links.jobbLabel")}
          </Link>
          <Link
            href="/cv"
            className="font-medium text-text-primary underline underline-offset-4 hover:no-underline"
          >
            {t("links.cvLabel")}
          </Link>
        </div>
      </>
    </InformationPageFrame>
  );
}
