import Link from "next/link";
import { getTranslations } from "next-intl/server";

/**
 * Root not-found — the boundary for UNMATCHED URLs across the whole site
 * (Next file convention: the root app/not-found handles every URL no route
 * matches, e.g. a guessed /cvmall). Renders in the root layout (fonts, theme,
 * i18n — no app shell, since an unmatched URL has no session context), with
 * civic Swedish (§10) and a way to the start page. notFound() calls INSIDE the
 * signed-in app are caught earlier by (app)/not-found.tsx, which keeps the
 * shell; this file is the last line.
 */
export default async function RootNotFound() {
  const t = await getTranslations("pages");

  return (
    // min-h-[60vh] + justify-center: this page renders chrome-less (no header/
    // footer to fill the viewport), so without a height floor the copy glues to
    // the top edge. Layout utility, not a locked design token.
    <main className="jp-container jp-page flex min-h-[60vh] flex-col justify-center gap-4">
      <h1 className="jp-h1">{t("common.notFound.title")}</h1>
      <p className="jp-lede">{t("common.notFound.body")}</p>
      <div>
        <Link href="/" className="jp-btn jp-btn--secondary">
          {t("common.notFound.toStart")}
        </Link>
      </div>
    </main>
  );
}
