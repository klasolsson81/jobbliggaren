import { Fragment } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { Database } from "lucide-react";
import { BrandLogo } from "@/components/brand/brand-logo";
import { LanguageSwitcher } from "@/components/i18n/language-switcher";

/**
 * SiteFooter — the shared deep-green footer (LP-3, #256). ONE footer mounted on
 * every shell: a brand column + three link columns + a thin closing bar.
 *
 * Civic-standard information architecture (#390, Klas 2026-06-29, ref AF/GOV.UK):
 *   • NO "Produkt" column — the deep app links (/jobb, /ansokningar, /cv,
 *     /matchningar) are auth-gated, so they redirect logged-out visitors to
 *     login and merely duplicate the header / app shell nav. The footer is for
 *     orientation (start, support, about) + a thin legal utility row, not a
 *     mirror of the primary navigation.
 *   • Legal/policy links (Användarvillkor, Integritet, Cookies, Tillgänglighet)
 *     live ONLY in the thin bottom bar (the civic "utility row" pattern), never
 *     in a content column — exactly one home for each, no duplication.
 *   • The about column is "Om Jobbliggaren" (Om, Kontakt, För utvecklare), not
 *     the old "Om och juridik" that mixed about + legal.
 *
 * Sync RSC: `useTranslations("landing")` resolves synchronously; `LanguageSwitcher`
 * is the only client island (the real cookie NEXT_LOCALE toggle, ADR 0078),
 * rendered with `variant="footer"` so it consumes the `.jp-foot__lang*` classes.
 *
 * All footer colour is LITERAL #FFFFFF / rgba(255,255,255,a) via the `.jp-foot*`
 * classes — never `--jp-ink-inverse` (it flips dark on the green in dark theme).
 *
 * Content routes not built yet (/hjalpcenter, /tillganglighet, /for-utvecklare)
 * render as aria-disabled, non-focusable spans (out of tab order) — no dead-link
 * window on any shell. They flip to live `<Link>`s once LP-8a/8b/8c ship.
 * start.register points at the live `/registrera` route (CTO verdict 2026-06-27):
 * a real route, forward-compatible once the open-registration flip lands.
 */

// Literal-union key types so next-intl keeps typed-message key-checking even
// though the keys are resolved dynamically in the map (same idiom as guest-shell).
type FooterHeadKey =
  | "footer.colStart"
  | "footer.colSupport"
  | "footer.colAbout";

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
  | "footer.about.developers";

type LegalLinkKey =
  | "footer.legal.terms"
  | "footer.legal.privacy"
  | "footer.legal.cookies"
  | "footer.legal.accessibility";

type FooterLink<K extends string> = {
  /** i18n key under `landing.footer.*` for the label. */
  readonly labelKey: K;
  /** Live route, or `null` for a not-yet-built route (renders aria-disabled). */
  readonly href: string | null;
};

type FooterColumn = {
  readonly key: string;
  readonly headKey: FooterHeadKey;
  readonly links: readonly FooterLink<FooterLinkKey>[];
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
      { labelKey: "footer.support.help", href: null },
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
      { labelKey: "footer.about.developers", href: null },
    ],
  },
];

// The thin bottom "utility row" — legal/policy links only (civic standard).
// Tillganglighet (/tillganglighet) is not built yet → aria-disabled.
const LEGAL: readonly FooterLink<LegalLinkKey>[] = [
  { labelKey: "footer.legal.terms", href: "/villkor" },
  { labelKey: "footer.legal.privacy", href: "/integritet" },
  { labelKey: "footer.legal.cookies", href: "/cookies" },
  { labelKey: "footer.legal.accessibility", href: null },
];

// Social accounts do not exist yet → aria-disabled text placeholders, out of tab
// order. Brand names are proper nouns (not localized); lucide-react ships no
// brand marks, so these are text-links per the issue, not icon buttons.
const SOCIAL = ["LinkedIn", "Facebook", "YouTube", "Instagram"] as const;

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
          <div className="flex flex-col gap-3">
            <nav
              className="jp-foot__legal"
              aria-label={t("footer.legalNavAriaLabel")}
            >
              {LEGAL.map((link, i) => (
                <Fragment key={link.labelKey}>
                  {i > 0 ? (
                    <span className="jp-foot__legal-sep" aria-hidden="true">
                      ·
                    </span>
                  ) : null}
                  {link.href ? (
                    <Link href={link.href}>{t(link.labelKey)}</Link>
                  ) : (
                    <span className="opacity-70" aria-disabled="true">
                      {t(link.labelKey)}
                    </span>
                  )}
                </Fragment>
              ))}
            </nav>
            <div className="jp-foot__legal">
              <span>{t("footer.copyright")}</span>
              <span className="jp-foot__legal-sep" aria-hidden="true">
                ·
              </span>
              <span>{t("footer.free")}</span>
            </div>
          </div>

          <div className="flex flex-wrap items-center gap-x-5 gap-y-2">
            <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
              <span>{t("footer.followUs")}</span>
              <ul className="flex flex-wrap items-center gap-x-3 gap-y-1">
                {SOCIAL.map((name) => (
                  <li key={name}>
                    <span className="opacity-70" aria-disabled="true">
                      {name}
                    </span>
                  </li>
                ))}
              </ul>
            </div>
            <LanguageSwitcher variant="footer" />
          </div>
        </div>
      </div>
    </footer>
  );
}
