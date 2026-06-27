"use client";

import { useTransition } from "react";
import { useRouter } from "next/navigation";
import { useLocale, useTranslations } from "next-intl";
import { locales, type Locale } from "@/i18n/routing";
import { setLocaleAction } from "@/i18n/set-locale-action";

/**
 * Civic SV/EN language switcher. Sets the `NEXT_LOCALE` cookie via a Server
 * Action, then refreshes so the server re-renders in the chosen locale (the app
 * uses next-intl without i18n routing, so the URL never changes).
 *
 * Civic-utility: no flags, no emoji — short language codes (SV/EN) with the full
 * language name as the accessible name. `role="group"` with `aria-pressed`
 * buttons; keyboard-accessible. Placement is the landing footer and settings,
 * never the header (HANDOVER §0.7). See ADR 0078.
 *
 * `variant="footer"` (LP-3, #256) renders the SAME cookie/a11y logic with the
 * deep-green footer's `.jp-foot__lang` / `.jp-foot__lang-btn` markup — literal-
 * white values that pass AA on the theme-stable `--jp-accent-900` (#0B2A1E),
 * consuming the dormant footer-lang classes #254 seeded. The default `.jp-lang*`
 * light-surface path (settings + the legacy landing footer) is unchanged (OCP).
 */
type LanguageSwitcherVariant = "default" | "footer";

export function LanguageSwitcher({
  variant = "default",
}: {
  variant?: LanguageSwitcherVariant;
}) {
  const t = useTranslations("common.languageSwitcher");
  const active = useLocale() as Locale;
  const router = useRouter();
  const [isPending, startTransition] = useTransition();

  function select(next: Locale) {
    if (next === active || isPending) return;
    startTransition(async () => {
      await setLocaleAction(next);
      router.refresh();
    });
  }

  const isFooter = variant === "footer";
  const groupClass = isFooter ? "jp-foot__lang" : "jp-lang";
  const btnClass = isFooter ? "jp-foot__lang-btn" : "jp-lang__btn";

  return (
    <div className={groupClass} role="group" aria-label={t("label")}>
      {locales.map((loc) => (
        <button
          key={loc}
          type="button"
          className={btnClass}
          aria-pressed={active === loc}
          aria-label={t(loc)}
          disabled={isPending}
          onClick={() => select(loc)}
        >
          {loc.toUpperCase()}
        </button>
      ))}
    </div>
  );
}
