import Link from "next/link";
import { getTranslations } from "next-intl/server";
import type { Metadata } from "next";
import { notFoundMetadata } from "@/lib/metadata/not-found-title";

export async function generateMetadata(): Promise<Metadata> {
  return notFoundMetadata();
}

/**
 * (public-job)/not-found — 404-gränsen för den publika jobbsidan (ADR 0165). Sidan anropar `notFound()` när dess
 * egen läsning visar att annonsen inte längre är listad efter att kanten svarat 200 (annonsen hann upphöra
 * emellan). Ett okänt eller aldrig publicerat id når inte hit: kanten skriver om det till en path ingen route
 * matchar, och rotens `not-found` svarar. Renderas som layoutens barn, så ramen överlever.
 */
export default async function PublicJobNotFound() {
  const t = await getTranslations("fallback");

  return (
    <main id="main" tabIndex={-1} className="jp-container jp-page flex flex-col gap-4 focus:outline-none">
      <h1 className="jp-h1">{t("notFound.title")}</h1>
      <p className="jp-lede">{t("notFound.body")}</p>
      <div>
        <Link href="/" className="jp-btn jp-btn--secondary">
          {t("notFound.toStart")}
        </Link>
      </div>
    </main>
  );
}
