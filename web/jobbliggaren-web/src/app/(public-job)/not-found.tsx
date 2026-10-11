import Link from "next/link";
import { getTranslations } from "next-intl/server";
import type { Metadata } from "next";
import { notFoundMetadata } from "@/lib/metadata/not-found-title";

export async function generateMetadata(): Promise<Metadata> {
  return notFoundMetadata();
}

/**
 * (public-job)/not-found: the 404 boundary of the public job page (ADR 0165). The page calls `notFound()` when its own
 * read finds the ad no longer listed after the edge answered 200. An unknown or never-published id does not reach here:
 * the edge rewrites it to a path no route matches, and the root `not-found` answers. Rendered as the layout's children,
 * so the frame survives.
 */
export default async function PublicJobNotFound() {
  const t = await getTranslations("fallback");

  return (
    <main
      id="main"
      tabIndex={-1}
      className="jp-container jp-page flex min-h-[60vh] flex-col justify-center gap-4 focus:outline-none"
    >
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
