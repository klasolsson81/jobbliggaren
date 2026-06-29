import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("content-legal");
  return {
    title: t("contact.meta.title"),
    description: t("contact.meta.description"),
  };
}

/**
 * Publik innehållssida: Kontakta oss (#262). Statisk RSC. Återbrukar
 * (marketing-inner)-mönstret. v1 = e-postkontakt (mailto), inget formulär:
 * e-postutskick är prod-gejtat (Resend, ADR 0080), så ett formulär kan inte
 * leverera än. Formuläret byggs när e-post slås på. Civic-utility: en h1,
 * hög-kontrast text, ingen em-dash, inget utropstecken.
 */
export default async function KontaktPage() {
  const t = await getTranslations("content-legal");
  const aboutList = t.raw("contact.aboutList") as string[];
  const email = t("contact.email");

  return (
    <main id="main" tabIndex={-1} className="focus:outline-none">
      <section className="jp-pagehero" aria-labelledby="kontakt-heading">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            <h1 id="kontakt-heading" className="jp-pagehero__title">
              {t("contact.title")}
            </h1>
          </div>
        </div>
      </section>

      <div className="mx-auto w-full max-w-2xl px-6 py-12">
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
      </div>
    </main>
  );
}
