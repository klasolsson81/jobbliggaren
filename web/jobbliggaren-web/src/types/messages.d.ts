// Typed messages for next-intl (v4): augment `AppConfig` so `useTranslations`
// and `getTranslations` namespace/key arguments are checked against the Swedish
// catalog (the source of truth). See https://next-intl.dev/docs/workflows/typescript.
import type svMessages from "../../messages/sv";
import type { Locale } from "@/i18n/routing";

// Widen leaf message values from their string-literal types to `string`. This
// PRESERVES namespace/key type-checking (the key structure is unchanged, so
// typos and missing keys are still caught) while preventing next-intl from
// instantiating ICU-argument types from each literal message string. That
// per-message literal parsing is what makes TypeScript report "Type
// instantiation is excessively deep and possibly infinite" (TS2589) on
// `t(key, values)` calls once the catalog is large and Next.js injects its
// route-type validators during `next build`. ICU arguments remain validated at
// runtime by next-intl (and surfaced by tests and the production build).
type WidenMessageLeaves<T> = T extends string
  ? string
  : { [K in keyof T]: WidenMessageLeaves<T[K]> };

declare module "next-intl" {
  interface AppConfig {
    Locale: Locale;
    Messages: WidenMessageLeaves<typeof svMessages>;
  }
}
