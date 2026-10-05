import { InformationPageFrame } from "@/components/information/InformationPageFrame";
import type { Metadata } from "next";
import { InformationLink as Link } from "@/components/information/InformationLink";
import { getTranslations } from "next-intl/server";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("content-legal");
  return {
    title: t("help.meta.title"),
    description: t("help.meta.description"),
  };
}

// Literal-union nyckel så next-intl behåller typad nyckel-kontroll i den
// dynamiska `help.items.${key}.*`-uppslagningen (samma idiom som FAQ_KEYS i
// /vanliga-fragor); `string` skulle tappa kontrollen och bryta tsc.
type HelpItemKey =
  | "faq"
  | "matching"
  | "cvReview"
  | "tips"
  | "contact"
  | "accessibility";

type HelpItem = { readonly key: HelpItemKey; readonly href: string };

type HelpGroup = {
  readonly key: string;
  readonly headingKey: "help.guidesHeading" | "help.moreHeading";
  readonly introKey: "help.guidesIntro" | "help.moreIntro";
  readonly items: readonly HelpItem[];
};

// Hubblänkar: hrefs hör hemma i koden (routing-angelägenhet), etiketter och
// beskrivningar i i18n (paritetstestat — samma idiom som SiteFooter). Hjälpcenter
// speglar footerns stöd-kolumn (vanliga frågor, matchning, cv-granskning, tips)
// och lägger till kontakt och tillgänglighet. Det länkar VIDARE till de
// befintliga sidorna, det duplicerar inte deras innehåll.
const GROUPS: readonly HelpGroup[] = [
  {
    key: "guides",
    headingKey: "help.guidesHeading",
    introKey: "help.guidesIntro",
    items: [
      { key: "faq", href: "/vanliga-fragor" },
      { key: "matching", href: "/matchning" },
      { key: "cvReview", href: "/cv-granskning" },
      { key: "tips", href: "/tips" },
    ],
  },
  {
    key: "more",
    headingKey: "help.moreHeading",
    introKey: "help.moreIntro",
    items: [
      { key: "contact", href: "/kontakt" },
      { key: "accessibility", href: "/tillganglighet" },
    ],
  },
];


export default async function HjalpcenterPage() {
  const t = await getTranslations("content-legal");

  return (
    <InformationPageFrame title={t("help.title")} lede={t("help.lede")} headingId="hjalpcenter-heading">

      <>
        <div className="flex flex-col gap-10">
          {GROUPS.map((group) => (
            <div key={group.key}>
              <h2 className="text-body-lg font-semibold text-text-primary">
                {t(group.headingKey)}
              </h2>
              <p className="mt-3 text-body text-text-primary">
                {t(group.introKey)}
              </p>
              <ul className="mt-4 flex flex-col gap-5">
                {group.items.map((item) => (
                  <li key={item.key}>
                    <Link
                      id={`information-help-${item.key}`}
                      href={item.href}
                      className="text-body font-semibold text-text-primary underline"
                    >
                      {t(`help.items.${item.key}.label`)}
                    </Link>
                    <p className="mt-1 text-body text-text-primary">
                      {t(`help.items.${item.key}.description`)}
                    </p>
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </div>
      </>
    </InformationPageFrame>
  );
}
