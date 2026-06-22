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
 */
export function LanguageSwitcher() {
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

  return (
    <div className="jp-lang" role="group" aria-label={t("label")}>
      {locales.map((loc) => (
        <button
          key={loc}
          type="button"
          className="jp-lang__btn"
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
