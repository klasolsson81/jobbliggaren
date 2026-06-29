import type { Metadata } from "next";
import Link from "next/link";
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

/**
 * Publik innehållssida: Tips för jobbsökande (#261). Statisk RSC. Återbrukar
 * (marketing-inner)-mönstret (delad SiteHeader/SiteFooter; eget
 * `<main id="main">` som skip-mål per #284). Civic-utility: en h1, hög-kontrast
 * text, ingen em-dash, inget utropstecken. Ingen HowTo/Article-strukturdata i
 * v1 (FAQPage är det enda schemat på sajten). Interna länkar till /jobb och /cv
 * (auth-gated; oinloggad besökare slussas till inloggning).
 */
export default async function TipsPage() {
  const t = await getTranslations("content-tips");
  const sections = TIP_KEYS.map((key) => ({
    title: t(`sections.${key}.title`),
    body: t(`sections.${key}.body`),
  }));

  return (
    <main id="main" tabIndex={-1} className="focus:outline-none">
      <section className="jp-pagehero" aria-labelledby="tips-heading">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            <h1 id="tips-heading" className="jp-pagehero__title">
              {t("title")}
            </h1>
          </div>
        </div>
      </section>

      <div className="mx-auto w-full max-w-2xl px-6 py-12">
        <p className="text-body text-text-primary">{t("intro")}</p>
        <div className="mt-8 flex flex-col gap-8">
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
      </div>
    </main>
  );
}
