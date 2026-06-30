import type { Metadata } from "next";
import Link from "next/link";
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

/**
 * Publik innehållssida: Hjälpcenter (#262). Statisk RSC, samma mönster som
 * /om, /kontakt och /tillganglighet (delad SiteHeader/SiteFooter via layouten;
 * eget `<main id="main">` som skip-mål per #284). Innehållet drivs ur
 * `content-legal`-katalogen (sv = källa, en speglad, paritetstestad).
 *
 * Hjälpcenter är en HUBB: den samlar hjälpen på ett ställe och länkar vidare till
 * de befintliga hjälp- och guidesidorna (vanliga frågor, så fungerar matchningen,
 * så granskar vi ditt cv, tips) plus kontakt och tillgänglighet. Den duplicerar
 * inte deras innehåll — varje rad är en länk med en mening som säger vad sidan
 * ger. Civic-utility: en h1, hög-kontrast text (ingen grå brödtext), ingen
 * em-dash, inget utropstecken.
 */
export default async function HjalpcenterPage() {
  const t = await getTranslations("content-legal");

  return (
    <main id="main" tabIndex={-1} className="focus:outline-none">
      <section className="jp-pagehero" aria-labelledby="hjalpcenter-heading">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            <h1 id="hjalpcenter-heading" className="jp-pagehero__title">
              {t("help.title")}
            </h1>
          </div>
        </div>
      </section>

      <div className="mx-auto w-full max-w-2xl px-6 py-12">
        <p className="text-body text-text-primary">{t("help.intro")}</p>

        <div className="mt-10 flex flex-col gap-10">
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
      </div>
    </main>
  );
}
