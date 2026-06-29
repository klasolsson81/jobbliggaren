import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("content-faq");
  return {
    title: t("meta.title"),
    description: t("meta.description"),
  };
}

// Fast nyckel-ordning för Vanliga frågor. Driver BÅDE den synliga listan och
// FAQPage-strukturdatan från SAMMA källa (i18n-katalogen) så de aldrig kan
// drifta isär (Google flaggar JSON-LD som inte matchar synligt innehåll).
const FAQ_KEYS = [
  "pris",
  "kalla",
  "matchning",
  "ai",
  "konto",
  "uppgifter",
  "cv",
  "radera",
] as const;

/**
 * Publik innehållssida: Vanliga frågor (#261). Statisk RSC, ingen
 * klient-interaktivitet. Återbrukar (marketing-inner)-mönstret (delad
 * SiteHeader/SiteFooter via layouten; eget `<main id="main">` som skip-mål per
 * #284). Civic-utility: en h1, hög-kontrast text (ingen grå), ingen em-dash,
 * inget utropstecken.
 *
 * FAQPage JSON-LD (det enda strukturdata-schemat på sajten i v1) byggs från
 * samma `FAQ_KEYS`/i18n-källa som den synliga `<dl>` så de inte kan divergera.
 */
export default async function VanligaFragorPage() {
  const t = await getTranslations("content-faq");
  const items = FAQ_KEYS.map((key) => ({
    q: t(`items.${key}.q`),
    a: t(`items.${key}.a`),
  }));

  const faqJsonLd = {
    "@context": "https://schema.org",
    "@type": "FAQPage",
    mainEntity: items.map((item) => ({
      "@type": "Question",
      name: item.q,
      acceptedAnswer: {
        "@type": "Answer",
        text: item.a,
      },
    })),
  };

  return (
    <main id="main" tabIndex={-1} className="focus:outline-none">
      <section className="jp-pagehero" aria-labelledby="faq-heading">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            <h1 id="faq-heading" className="jp-pagehero__title">
              {t("title")}
            </h1>
          </div>
        </div>
      </section>

      <div className="mx-auto w-full max-w-2xl px-6 py-12">
        <p className="text-body text-text-primary">{t("intro")}</p>
        <dl className="mt-8 flex flex-col gap-8">
          {items.map((item, index) => (
            <div key={FAQ_KEYS[index]}>
              <dt className="text-body font-semibold text-text-primary">
                {item.q}
              </dt>
              <dd className="mt-2 text-body text-text-primary">{item.a}</dd>
            </div>
          ))}
        </dl>
      </div>

      {/* FAQPage strukturdata. Egen, betrodd copy (ingen användarinput); JSON
          serialiseras och `<`-tecken escapas defensivt mot script-stängning. */}
      <script
        type="application/ld+json"
        dangerouslySetInnerHTML={{
          __html: JSON.stringify(faqJsonLd).replace(/</g, "\\u003c"),
        }}
      />
    </main>
  );
}
