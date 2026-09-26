import Image from "next/image";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import { externalLoginStartHref, type ExternalProviderKey } from "@/lib/auth/external-login";

// The three identity providers (ADR 0142 D8). A Server Component: a row that starts a login is a
// link, and a row that does nothing needs no client code either.
//
// An inactive row is `aria-disabled`, never `disabled`: a disabled button leaves the tab order and
// the accessibility tree, and with it the one sentence that explains why nothing happens. What says
// "not yet" is the text and the attribute, never `opacity`. Its outline hover is cancelled: a row
// that lights up promises a press it cannot keep. It carries no mark.
//
// An active row is an `<a href>`, never a form and never `next/link`: the CSP's `form-action` would
// refuse a form that navigates to the provider, and a prefetch must not start a flow. It carries the
// provider's official mark, unmodified (DESIGN.md §3). Its hover shows on the border, so the mark's
// white ground matches the row in rest and in hover (design-reviewer D2).
//
// Reach order for a Swedish job seeker, not alphabetical.
const PROVIDERS = ["google", "linkedin", "github"] as const;

/**
 * The provider's asset, byte-identical and with exactly one consumer: this file
 * (`provider-marks.test.ts`). Rendered 40x40 in a 20x20 window, so the asset's own frame falls
 * outside it by layout rather than by an edited file.
 */
const MARKS: Readonly<Record<ExternalProviderKey, string>> = {
  google: "/provider-marks/google-g-light-square-4x.png",
};

export function ProviderButtons({
  active = [],
  next = "",
  describedBy,
}: {
  /** The providers the api registered; the rest stay inactive. */
  active?: readonly ExternalProviderKey[];
  /** The post-login path, carried to the start as given; the start guards it. */
  next?: string;
  /** The id of the line an active row's action is described by. */
  describedBy?: string;
}) {
  const t = useTranslations("pages");

  return (
    <ul className="flex flex-col gap-3">
      {PROVIDERS.map((provider) => {
        const live = active.find((key) => key === provider);
        return (
          <li key={provider}>
            {live === undefined ? (
              // The accessible name is computed from the content: "Fortsätt med LinkedIn Kommer snart".
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
            ) : (
              <Button
                asChild
                variant="outline"
                className="h-auto min-h-10 w-full justify-start gap-3 py-2 text-left whitespace-normal hover:border-brand-700 hover:bg-background max-md:min-h-11"
              >
                <a href={externalLoginStartHref(live, next)} aria-describedby={describedBy}>
                  <span aria-hidden="true" className="relative size-5 shrink-0 overflow-hidden">
                    <Image
                      src={MARKS[live]}
                      alt=""
                      width={40}
                      height={40}
                      unoptimized
                      loading="eager"
                      className="absolute -top-2.5 -left-2.5 h-10 w-10 max-w-none"
                    />
                  </span>
                  <span>{t(`auth.passwordless.entry.providers.${live}`)}</span>
                </a>
              </Button>
            )}
          </li>
        );
      })}
    </ul>
  );
}
