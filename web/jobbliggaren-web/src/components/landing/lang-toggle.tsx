import { LanguageSwitcher } from "@/components/i18n/language-switcher";

/**
 * Språk-toggle (SV/EN) för landing-footern.
 *
 * HANDOVER §0 punkt 7 + §6.4: toggle placeras EJ i header — endast i
 * landing-footer + Inställningar. Den live next-intl-växlaren bor i
 * `@/components/i18n/language-switcher`; denna tunna wrapper behåller det
 * tidigare publika namnet `LandingLangToggle` så landing-footerns call-site
 * är oförändrad.
 */
export function LandingLangToggle() {
  return <LanguageSwitcher />;
}
