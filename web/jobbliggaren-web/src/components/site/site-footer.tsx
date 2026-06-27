import Link from "next/link";
import { useTranslations } from "next-intl";
import { Database } from "lucide-react";
import { BrandLogo } from "@/components/brand/brand-logo";
import { LanguageSwitcher } from "@/components/i18n/language-switcher";

/**
 * SiteFooter — the shared deep-green footer (LP-3, #256). ONE footer mounted on
 * every shell: a brand column + four link columns + a thin closing row.
 *
 * Sync RSC: `useTranslations("landing")` resolves synchronously; `LanguageSwitcher`
 * is the only client island (the real cookie NEXT_LOCALE toggle, ADR 0078),
 * rendered with `variant="footer"` so it consumes the dormant `.jp-foot__lang*`
 * classes #254 seeded. We render `LanguageSwitcher` DIRECTLY (the canonical
 * functional toggle) rather than the landing-scoped `LandingLangToggle` wrapper,
 * because that wrapper still drives the legacy light-surface `landing-footer.tsx`
 * (deleted only in LP-4/#257) — repurposing it to the footer variant would turn
 * the legacy footer's toggle white-on-light = illegible (a §12 broken window in
 * the #256→#257 window). The shared site footer also should not depend on a
 * landing-namespace component.
 *
 * All footer colour is LITERAL #FFFFFF / rgba(255,255,255,a) via the `.jp-foot*`
 * classes — never `--jp-ink-inverse` (it flips dark on the green in dark theme).
 *
 * Content routes not built yet (/om, /kontakt, /hjalpcenter, /vanliga-fragor,
 * /tips, /tillganglighet, /integritet, /for-utvecklare) and Paminnelser render
 * as aria-disabled, non-focusable spans (out of tab order) — no dead-link window
 * on any shell. They flip to live `<Link>`s once LP-8a/8b/8c ship. start.register
 * points at the live `/registrera` route (CTO verdict 2026-06-27): a real route,
 * forward-compatible — it auto-corrects once the open-registration flip lands.
 *
 * K3 dedupe: legal links (Villkor/Cookies/Tillganglighet) live ONLY in the
 * columns; the closing row carries only copyright + the single free line +
 * social + the language toggle. No duplicated legal bar.
 */

// Literal-union key types so next-intl keeps typed-message key-checking even
// though the keys are resolved dynamically in the map (same idiom as guest-shell).
type FooterHeadKey =
  | "footer.colProduct"
  | "footer.colStart"
  | "footer.colSupport"
  | "footer.colLegal";

type FooterLinkKey =
  | "footer.product.jobs"
  | "footer.product.applications"
  | "footer.product.cv"
  | "footer.product.matches"
  | "footer.product.reminders"
  | "footer.start.register"
  | "footer.start.login"
  | "footer.start.guest"
  | "footer.support.help"
  | "footer.support.faq"
  | "footer.support.tips"
  | "footer.support.accessibility"
  | "footer.legal.about"
  | "footer.legal.contact"
  | "footer.legal.terms"
  | "footer.legal.privacy"
  | "footer.legal.cookies"
  | "footer.legal.developers";

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
// mirroring the issue's content-route gating (LP-8a/8b/8c) + Paminnelser.
const COLUMNS: readonly FooterColumn[] = [
  {
    key: "product",
    headKey: "footer.colProduct",
    links: [
      { labelKey: "footer.product.jobs", href: "/jobb" },
      { labelKey: "footer.product.applications", href: "/ansokningar" },
      { labelKey: "footer.product.cv", href: "/cv" },
      { labelKey: "footer.product.matches", href: "/matchningar" },
      { labelKey: "footer.product.reminders", href: null },
    ],
  },
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
      { labelKey: "footer.support.faq", href: null },
      { labelKey: "footer.support.tips", href: null },
      { labelKey: "footer.support.accessibility", href: null },
    ],
  },
  {
    key: "legal",
    headKey: "footer.colLegal",
    links: [
      { labelKey: "footer.legal.about", href: null },
      { labelKey: "footer.legal.contact", href: null },
      { labelKey: "footer.legal.terms", href: "/villkor" },
      { labelKey: "footer.legal.privacy", href: null },
      { labelKey: "footer.legal.cookies", href: "/cookies" },
      { labelKey: "footer.legal.developers", href: null },
    ],
  },
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
          <div className="jp-foot__legal">
            <span>{t("footer.copyright")}</span>
            <span className="jp-foot__legal-sep" aria-hidden="true">
              ·
            </span>
            <span>{t("footer.free")}</span>
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
