import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";

// The three identity providers, visible and inactive until part 6a gives one of them keys
// (Klas, 2026-09-17). A Server Component: a row that does nothing needs no client code.
//
// `aria-disabled`, never `disabled`: a disabled button leaves the tab order and the accessibility
// tree, and with it the one sentence that explains why nothing happens. What says "not yet" is the
// text and the attribute, never `opacity`, so contrast is what it is everywhere else. The outline
// variant's hover is cancelled: a row that lights up promises a press it cannot keep.
//
// No provider mark while inactive (design-reviewer, #1738). lucide ships no brand icons, so a
// mark here would be a hand-drawn copy of a third party's trademark on a control that logs nobody
// in. The question of marks is 6a's.
//
// Reach order for a Swedish job seeker, not alphabetical.
const PROVIDERS = ["google", "linkedin", "github"] as const;

export function ProviderButtons() {
  const t = useTranslations("pages");

  return (
    <ul className="flex flex-col gap-3">
      {PROVIDERS.map((provider) => (
        <li key={provider}>
          {/* The accessible name is computed from the content: "Fortsätt med Google Kommer snart". */}
          <Button
            type="button"
            variant="outline"
            aria-disabled="true"
            className="h-auto min-h-10 w-full cursor-default justify-between gap-3 py-2 text-left whitespace-normal hover:bg-background max-md:min-h-11"
          >
            <span>{t(`auth.passwordless.entry.providers.${provider}`)}</span>
            <span className="text-body-sm text-text-primary">
              {t("auth.passwordless.entry.providers.comingSoon")}
            </span>
          </Button>
        </li>
      ))}
    </ul>
  );
}
