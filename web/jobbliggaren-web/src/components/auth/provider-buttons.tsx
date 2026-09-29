import Image from "next/image";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import {
  EXTERNAL_PROVIDER_KEYS,
  externalLoginStartHref,
  type ExternalProviderKey,
} from "@/lib/auth/external-login";

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
// provider's official mark, unmodified, where DESIGN.md §3 admits one. Its hover shows on the border, so
// the mark's white ground matches the row in rest and in hover (design-reviewer D2).

type Mark = { src: string; px: number; className: string };

/**
 * Each provider's asset, byte-identical and with exactly one consumer: this file
 * (`provider-marks.test.ts`), always in a 20x20 slot. Google's is rendered 40x40 in that window, so
 * the asset's own frame falls outside it by layout rather than by an edited file; GitHub's has no
 * frame and is fitted whole, never stretched out of its 294:288 ratio. LinkedIn's row has none.
 */
const MARKS: Readonly<Record<ExternalProviderKey, Mark | null>> = {
  google: {
    src: "/provider-marks/google-g-light-square-4x.png",
    px: 40,
    className: "absolute -top-2.5 -left-2.5 h-10 w-10 max-w-none",
  },
  linkedin: null,
  github: {
    src: "/provider-marks/github-invertocat-black.png",
    px: 20,
    className: "size-5 object-contain",
  },
};

export function ProviderButtons({
  active = [],
  next = "",
}: {
  /** The providers the api registered; the rest stay inactive. */
  active?: readonly ExternalProviderKey[];
  /** The post-login path, carried to the start as given; the start guards it. */
  next?: string;
}) {
  const t = useTranslations("pages");

  // The Button primitive lifts every row to 44 px at ≤768 px (DESIGN.md §5).
  return (
    <ul className="flex flex-col gap-2">
      {EXTERNAL_PROVIDER_KEYS.map((provider) => {
        const live = active.find((key) => key === provider);
        const mark = live === undefined ? null : MARKS[live];
        return (
          <li key={provider}>
            {live === undefined ? (
              // The accessible name is computed from the content: "Fortsätt med LinkedIn Kommer snart".
              <Button
                type="button"
                variant="outline"
                aria-disabled="true"
                className="h-auto min-h-10 w-full cursor-default justify-between gap-3 px-3 py-1.5 text-left whitespace-normal hover:bg-background"
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
                className="h-auto min-h-10 w-full justify-center gap-3 px-3 py-1.5 text-center whitespace-normal hover:border-brand-700 hover:bg-background"
              >
                <a href={externalLoginStartHref(live, next)}>
                  {mark === null ? null : (
                    <span aria-hidden="true" className="relative size-5 shrink-0 overflow-hidden">
                      <Image
                        src={mark.src}
                        alt=""
                        width={mark.px}
                        height={mark.px}
                        unoptimized
                        loading="eager"
                        className={mark.className}
                      />
                    </span>
                  )}
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
