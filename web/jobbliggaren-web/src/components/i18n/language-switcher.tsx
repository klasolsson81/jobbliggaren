"use client";

import { useTransition } from "react";
import { useRouter } from "next/navigation";
import { useLocale, useTranslations } from "next-intl";
import { ChevronDown, Globe } from "lucide-react";
import { locales, isLocale, type Locale } from "@/i18n/routing";
import { setLocaleAction } from "@/i18n/set-locale-action";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";

/**
 * Civic language switcher. Sets the `NEXT_LOCALE` cookie via a Server Action,
 * then refreshes so the server re-renders in the chosen locale (the app uses
 * next-intl without i18n routing, so the URL never changes). See ADR 0078.
 *
 * **Placement: every surface whose user cannot reach Inställningar**
 * (senior-cto-advisor bind 2026-08-23, deriving Klas's HANDOVER-v3 §0 punkt 7
 * amendment). The rule it replaces sent theme and language toggles to
 * Inställningar and the landing footer and kept them out of every header. The
 * amendment lifts that for the language control, and the ground Klas gave is
 * what decides how far: a visitor who does not read Swedish cannot be expected
 * to find the control in the footer of a page they cannot read. `defaultLocale`
 * is `sv` and nothing negotiates Accept-Language, so that visitor gets Swedish —
 * which makes a surface with no control worse than the hard-to-find footer the
 * amendment was written to repair. `(app)` and `(admin)` keep their own Segment
 * in Inställningar and do NOT get this. The theme half of §0.7 is untouched.
 *
 * The HANDOVER is gitignored, so `site-header.test.tsx` and
 * `guest-shell.test.tsx` are the amended rule's only tracked readers.
 *
 * Two consumers, one presentation — the stronger form, because it shows the
 * presentation is context-independent. The `variant` prop is gone:
 * `"footer"` had a single mount that this change removes, and `"default"` had
 * none at all — Inställningar renders its own `Segment` (`display-card.tsx`),
 * never this component. A prop whose members no route reaches is the inert axis
 * §5/YAGNI rules out.
 *
 * Civic-utility: full language names, never "SV"/"EN" — a non-Swedish speaker
 * has to be able to recognise their own language. No flags, no emoji
 * (DESIGN.md §7). The list renders from `locales`, so a third locale needs no
 * change here.
 *
 * A11y: `DropdownMenuRadioItem` gives `role="menuitemradio"` + `aria-checked` —
 * picking a language is a choice, not an action, and `DropdownMenuItem` cannot
 * say so. Radix supplies the APG menu-button contract (roving focus, typeahead,
 * Escape, click-outside). The trigger's accessible name is "Språk" followed by
 * the current language, so it CONTAINS its visible label (WCAG 2.5.3) instead
 * of an `aria-label` that would replace it.
 */
export function LanguageSwitcher() {
  const t = useTranslations("common.languageSwitcher");
  const active = useLocale() as Locale;
  const router = useRouter();
  const [isPending, startTransition] = useTransition();

  function select(next: string) {
    if (!isLocale(next) || next === active || isPending) return;
    startTransition(async () => {
      await setLocaleAction(next);
      router.refresh();
    });
  }

  return (
    <DropdownMenu>
      <DropdownMenuTrigger className="jp-btn jp-btn--ghost" disabled={isPending}>
        <Globe size={18} aria-hidden="true" />
        <span className="sr-only">{t("label")}</span>
        <span className="jp-langtrigger__name">{t(active)}</span>
        <ChevronDown size={14} aria-hidden="true" />
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        <DropdownMenuRadioGroup value={active} onValueChange={select}>
          {locales.map((loc) => (
            <DropdownMenuRadioItem key={loc} value={loc}>
              {t(loc)}
            </DropdownMenuRadioItem>
          ))}
        </DropdownMenuRadioGroup>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
