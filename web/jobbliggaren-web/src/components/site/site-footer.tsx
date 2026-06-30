import Link from "next/link";
import { useTranslations } from "next-intl";
import { Database } from "lucide-react";
import { BrandLogo } from "@/components/brand/brand-logo";
import { LanguageSwitcher } from "@/components/i18n/language-switcher";

/**
 * SiteFooter — the shared deep-green footer (LP-3, #256). ONE footer mounted on
 * every shell: a brand column + four link columns + a thin closing bar.
 *
 * Civic information architecture (#390 → #393, Klas 2026-06-29):
 *   • NO "Produkt" column — the deep app links (/jobb, /ansokningar, /cv,
 *     /matchningar) are auth-gated, so they redirect logged-out visitors to
 *     login and merely duplicate the header / app shell nav. The footer is for
 *     orientation (start, support, about) + legal, not a mirror of the primary
 *     navigation.
 *   • FOUR even columns, ONE link style. #392 first moved legal into a thin
 *     bottom utility row, but with Produkt gone that left 3 sparse columns +
 *     a second (inline) link style → unbalanced. #393 makes legal its own
 *     "Juridik" column instead: four even columns (Kom igång / Stöd och guider /
 *     Om Jobbliggaren / Juridik), and the closing bar carries only copyright +
 *     the free line + the language toggle (no social block — social accounts
 *     are not coming for a long while, #393 — and no content links).
 *   • The about column is "Om Jobbliggaren" (Om, Kontakt, För utvecklare); the
 *     "Juridik" column carries the policy links (Villkor, Integritet, Cookies,
 *     Tillgänglighet) — the old "Om och juridik" mix is split into the two.
 *
 * Sync RSC: `useTranslations("landing")` resolves synchronously; `LanguageSwitcher`
 * is the only client island (the real cookie NEXT_LOCALE toggle, ADR 0078),
 * rendered with `variant="footer"` so it consumes the `.jp-foot__lang*` classes.
 *
 * All footer colour is LITERAL #FFFFFF / rgba(255,255,255,a) via the `.jp-foot*`
 * classes — never `--jp-ink-inverse` (it flips dark on the green in dark theme).
 *
 * Content routes shipped incrementally and ALL footer content routes are now
 * live `<Link>`s — there is currently no gated route, so no aria-disabled span
 * renders anywhere in the footer (/tillganglighet #263, /hjalpcenter #262,
 * /for-utvecklare #263). The `href: null` → aria-disabled-span mechanism below
 * is kept as forward-compat scaffolding for any FUTURE not-yet-built footer
 * link (CTO verdict 2026-06-30: the route series is demonstrably recurring, so
 * keeping the OCP extension point beats re-introducing it per route).
 * start.register points at the live `/registrera` route (CTO verdict 2026-06-27):
 * a real route, forward-compatible once the open-registration flip lands.
 */

// Literal-union key types so next-intl keeps typed-message key-checking even
// though the keys are resolved dynamically in the map (same idiom as guest-shell).
type FooterHeadKey =
  | "footer.colStart"
  | "footer.colSupport"
  | "footer.colAbout"
  | "footer.colLegal";

type FooterLinkKey =
  | "footer.start.register"
  | "footer.start.login"
  | "footer.start.guest"
  | "footer.support.help"
  | "footer.support.howMatching"
  | "footer.support.cvReview"
  | "footer.support.faq"
  | "footer.support.tips"
  | "footer.about.self"
  | "footer.about.contact"
  | "footer.about.developers"
  | "footer.legal.terms"
  | "footer.legal.privacy"
  | "footer.legal.cookies"
  | "footer.legal.accessibility";

type FooterLink = {
  /** i18n key under `landing.footer.*` for the label. */
  readonly labelKey: FooterLinkKey;
  /** Live route, or `null` for a not-yet-built route (renders aria-disabled). */
  readonly href: string | null;
};

type FooterColumn = {
  readonly key: string;
  readonly headKey: FooterHeadKey;
  readonly links: readonly FooterLink[];
};

// Live hrefs point at existing routes; `null` = not built yet → aria-disabled,
// mirroring the issue's content-route gating (LP-8a/8b/8c).
const COLUMNS: readonly FooterColumn[] = [
  {
    key: "start",
    headKey: "footer.colStart",
    links: [
      { labelKey: "footer.start.register", href: "/registrera" },
      { labelKey: "footer.start.login", href: "/logga-in" },
      { labelKey: "footer.start.guest", href: "/gast/oversikt" },
    ],
  },
  {
    key: "support",
    headKey: "footer.colSupport",
    links: [
      { labelKey: "footer.support.help", href: "/hjalpcenter" },
      { labelKey: "footer.support.howMatching", href: "/matchning" },
      { labelKey: "footer.support.cvReview", href: "/cv-granskning" },
      { labelKey: "footer.support.faq", href: "/vanliga-fragor" },
      { labelKey: "footer.support.tips", href: "/tips" },
    ],
  },
  {
    key: "about",
    headKey: "footer.colAbout",
    links: [
      { labelKey: "footer.about.self", href: "/om" },
      { labelKey: "footer.about.contact", href: "/kontakt" },
      { labelKey: "footer.about.developers", href: "/for-utvecklare" },
    ],
  },
  {
    key: "legal",
    headKey: "footer.colLegal",
    links: [
      { labelKey: "footer.legal.terms", href: "/villkor" },
      { labelKey: "footer.legal.privacy", href: "/integritet" },
      { labelKey: "footer.legal.cookies", href: "/cookies" },
      { labelKey: "footer.legal.accessibility", href: "/tillganglighet" },
    ],
  },
];

export function SiteFooter() {
  const t = useTranslations("landing");
  return (
    <footer className="jp-foot">
      <div className="jp-foot__main">
        <div className="jp-foot__cols">
          <div className="jp-foot__brandcol">
            <Link
              href="/"
              className="jp-brand"
              aria-label={t("brand.homeAriaLabel")}
            >
              <BrandLogo inverse />
            </Link>
            <p className="jp-foot__blurb">{t("footer.blurb")}</p>
            <p className="jp-foot__src">
              <Database size={14} aria-hidden="true" />
              {t("footer.source")}
            </p>
          </div>

          {COLUMNS.map((col) => {
            const headId = `jp-foot-${col.key}`;
            return (
              <nav key={col.key} aria-labelledby={headId}>
                <h2 id={headId} className="jp-foot__colhead">
                  {t(col.headKey)}
                </h2>
                <ul className="jp-foot__links">
                  {col.links.map((link) => (
                    <li key={link.labelKey}>
                      {link.href ? (
                        <Link href={link.href}>{t(link.labelKey)}</Link>
                      ) : (
                        <span className="opacity-70" aria-disabled="true">
                          {t(link.labelKey)}
                        </span>
                      )}
                    </li>
                  ))}
                </ul>
              </nav>
            );
          })}
        </div>
      </div>

      <div className="jp-foot__bar">
        <div className="jp-foot__bar-inner">
          <div className="jp-foot__legal">
            <span>{t("footer.copyright")}</span>
            <span className="jp-foot__legal-sep" aria-hidden="true">
              ·
            </span>
            <span>{t("footer.free")}</span>
          </div>

          <LanguageSwitcher variant="footer" />
        </div>
      </div>
    </footer>
  );
}
