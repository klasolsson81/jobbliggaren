import Link from "next/link";
import { getTranslations } from "next-intl/server";

/**
 * (guest)/gast/not-found — the 404 boundary for the guest mirrors. Four guest
 * pages call notFound() for an unknown mock id (/gast/jobb/[id],
 * /gast/ansokningar/[id] and both intercepting modals); without this file they
 * fell through to the ROOT not-found, which renders the public marketing frame
 * — the wrong shell for a visitor who is inside guest mode (#1477).
 *
 * Rendered as gast/layout's children, so GuestShell, its nav and the footer
 * survive. Placement mirrors (guest)/gast/error.tsx — see that file for why the
 * boundary cannot sit at the `(guest)` group root.
 */
export default async function GuestNotFound() {
  const t = await getTranslations("fallback");

  return (
    <div className="jp-container jp-page flex flex-col gap-4">
      <h1 className="jp-h1">{t("notFound.title")}</h1>
      <p className="jp-lede">{t("notFound.body")}</p>
      <div>
        <Link href="/gast/oversikt" className="jp-btn jp-btn--secondary">
          {t("notFound.toOverview")}
        </Link>
      </div>
    </div>
  );
}
