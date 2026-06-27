import Link from "next/link";
import { useTranslations } from "next-intl";
import { BrandLogo } from "@/components/brand/brand-logo";

/**
 * SiteHeader — the shared MINIMAL public header for the non-landing public
 * surfaces (auth `/logga-in` and the marketing-inner pages `/villkor`,
 * `/cookies`, `/vantelista`). LP-5a / #258, epic #267.
 *
 * It consumes the SAME shared `.jp-head` namespace as the landing header
 * (LP-4 / #257, `landing-header.tsx`) so the two public headers are visually
 * identical — a sticky white strip, border-bottom, brand left. The ONLY
 * difference is the right slot: the landing header shows live Platsbanken
 * stats; this minimal header shows an optional "Logga in" link (inner pages
 * never repeat stats outside the landing context). The legacy `.jp-land-top`
 * class this component used before #258 is now dormant (its last consumer);
 * the alias CSS is retired by the landing lane, not here.
 *
 * `variant="minimal"` (issue #258) is honored as INTENT — there is exactly one
 * public header variant in this wave, so the component simply IS the minimal
 * header; no inert single-member `variant` prop is added (senior-cto-advisor
 * bind, CLAUDE.md §5/YAGNI). The real axis is login-link visibility, modelled
 * by `showLogin`. A second variant is added if/when LP-5b folds the logged-in
 * shells in here.
 *
 * A11y: the brand + login links sit in a labelled `<nav>` landmark (the
 * surface's site navigation, distinct from the footer's section navs). The
 * skip link is rendered first, before the header, so every public surface that
 * mounts SiteHeader gets the same first-focusable jump to `#main` (the
 * adopting layouts expose that target). Mirrors the shells' skip-link pattern.
 */
export function SiteHeader({ showLogin = true }: { showLogin?: boolean }) {
  const t = useTranslations("landing");
  return (
    <>
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:absolute focus:top-2 focus:left-2 focus:z-50 focus:rounded-sm focus:bg-surface-secondary focus:px-3 focus:py-2 focus:text-body-sm focus:text-text-primary focus:outline-2 focus:outline-offset-2 focus:outline-ring"
      >
        {t("common.skipToContent")}
      </a>
      <header className="jp-head">
        <nav
          className="jp-head__inner"
          aria-label={t("common.headerNavAriaLabel")}
        >
          <Link href="/" className="jp-brand" aria-label={t("brand.homeAriaLabel")}>
            <BrandLogo />
          </Link>
          {showLogin && (
            <Link
              href="/logga-in"
              className="text-body-sm font-medium text-text-primary underline-offset-4 hover:underline"
            >
              {t("common.loginLink")}
            </Link>
          )}
        </nav>
      </header>
    </>
  );
}
