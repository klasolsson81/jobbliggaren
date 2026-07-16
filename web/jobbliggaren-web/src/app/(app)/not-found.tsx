import Link from "next/link";
import { getTranslations } from "next-intl/server";

/**
 * (app)/not-found — the signed-in app's 404 boundary (design-reviewer Major on
 * PR #906, adversarially confirmed: no not-found.tsx existed anywhere, so every
 * notFound() in the app — a missing resume, the retired /cv/[id]/mall stub —
 * fell through to Next's root default: bare English copy OUTSIDE the shell,
 * with no way back). This boundary renders as the layout's children, so the
 * shell, navigation and theme stay intact, the copy is civic Swedish (§10),
 * and there is always a way home. Session gating is inherited from
 * (app)/layout, which runs before children render.
 */
export default async function AppNotFound() {
  const t = await getTranslations("pages");

  return (
    // jp-container jp-page assumes every notFound() caller is a v3-native route
    // (/cv, /jobb, /ansokningar — AppShell skips its transitional container there).
    // If a NON-v3-native route (/installningar, /matchningar, …) ever calls
    // notFound(), this boundary double-wraps (shell container + this one) —
    // re-evaluate the wrap then (design-reviewer Minor, PR 2b).
    <div className="jp-container jp-page flex flex-col gap-4">
      <h1 className="jp-h1">{t("common.notFound.title")}</h1>
      <p className="jp-lede">{t("common.notFound.body")}</p>
      <div>
        <Link href="/oversikt" className="jp-btn jp-btn--secondary">
          {t("common.notFound.toOverview")}
        </Link>
      </div>
    </div>
  );
}
