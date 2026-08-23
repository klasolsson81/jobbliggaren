import { useId, type ComponentProps } from "react";
import Link from "next/link";
import { Check } from "lucide-react";
import { useTranslations } from "next-intl";

// Required terms + privacy acceptance for the register form (#1479).
//
// Shape copied from RememberMeCheckbox, for the same reason: RegisterForm is an
// uncontrolled `<form action={serverAction}>`, so the value has to travel in
// FormData (a checked native checkbox posts "on", unchecked posts nothing).
// That rules out the controlled widgets (ToggleRow, jp-checkitem), and the
// native input carries keyboard operation, the global :focus-visible ring and
// checkbox semantics for free.
//
// Unticked by default: a pre-ticked box is not acceptance the user performed.
//
// The two policy links sit INSIDE the label, which is safe — the HTML standard
// suppresses a label's activation behavior for events targeted at interactive
// descendants, so following a link does not also toggle the box. They open in a
// new tab so a half-filled registration survives the detour, and that warning
// lives in the hint rather than in each link's own accessible name: the links
// are part of the label, so an "(öppnas i ny flik)" per link would be read back
// twice inside the checkbox's own name.
//
// Props spread onto the input the way `ui/input.tsx` does, so the form owns
// `ref` and `aria-invalid` — but the spread goes FIRST, so `required` and the
// posted name stay this component's to decide and no call site can switch the
// gate off. A caller-supplied `aria-describedby` (the error) is prepended by
// the hint rather than replaced by it.
export function AcceptTermsCheckbox({
  "aria-describedby": describedBy,
  ...props
}: ComponentProps<"input">) {
  const t = useTranslations("pages");
  const hintId = useId();
  return (
    <div className="flex flex-col gap-1.5">
      <label className="flex min-h-11 cursor-pointer items-center gap-2.5 text-label font-medium text-text-primary">
        <span className="relative inline-flex shrink-0 items-center justify-center">
          <input
            {...props}
            name="acceptTerms"
            type="checkbox"
            required
            aria-required="true"
            aria-describedby={describedBy ? `${hintId} ${describedBy}` : hintId}
            className="peer size-5 cursor-pointer appearance-none rounded-sm border-2 border-border-strong bg-surface-primary transition-colors duration-75 checked:border-brand-600 checked:bg-brand-600 disabled:cursor-not-allowed disabled:opacity-50"
          />
          <Check
            aria-hidden="true"
            strokeWidth={3}
            className="pointer-events-none absolute hidden size-3.5 text-white peer-checked:block"
          />
        </span>
        <span>
          {t.rich("auth.register.termsLabel", {
            terms: (chunks) => (
              <Link
                href="/villkor"
                target="_blank"
                rel="noopener noreferrer"
                className="text-brand-600 underline underline-offset-2 hover:text-brand-700"
              >
                {chunks}
              </Link>
            ),
            privacy: (chunks) => (
              <Link
                href="/integritet"
                target="_blank"
                rel="noopener noreferrer"
                className="text-brand-600 underline underline-offset-2 hover:text-brand-700"
              >
                {chunks}
              </Link>
            ),
          })}
        </span>
      </label>
      {/* text-text-primary (not -secondary) to match the sibling field hints in the same
          form and honour the high-contrast, no-muted-text copy rule. */}
      <p id={hintId} className="text-body-sm text-text-primary">
        {t("auth.register.termsHint")}
      </p>
    </div>
  );
}
