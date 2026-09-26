import { useId, type ComponentProps, type ReactNode } from "react";
import Link from "next/link";
import { Check } from "lucide-react";
import { useTranslations } from "next-intl";

/** A `t.rich` tag renderer for a policy link that opens in a new tab. */
function policyLink(href: string) {
  return function renderPolicyLink(chunks: ReactNode) {
    return (
      <Link
        href={href}
        target="_blank"
        rel="noopener noreferrer"
        className="py-1.5 text-brand-600 underline underline-offset-2 hover:text-brand-700"
      >
        {chunks}
      </Link>
    );
  };
}

// The login flow's consent step (ADR 0142 D6): the box accepts the TERMS and nothing else. The
// privacy policy is an Art. 13 notice the user is pointed to, never something they accept, so it sits
// in a sibling sentence under the box and outside the checkbox's own name.
//
// A native checkbox, because the consent form is an uncontrolled `<form action={serverAction}>` and
// the value has to travel in FormData (a checked box posts "on", an unchecked one posts nothing). The
// native input also carries keyboard operation, the global :focus-visible ring and checkbox semantics.
//
// Unticked by default: a pre-ticked box is not acceptance the user performed.
//
// The terms link sits INSIDE the label, which is safe: the HTML standard suppresses a label's
// activation behaviour for events targeted at interactive descendants, so following the link does not
// also toggle the box. The links open in a new tab so the step survives the detour, and that warning
// lives in the hint rather than in the link's accessible name, which is part of the checkbox's own name.
//
// The props are narrowed to the three the form supplies, so a call site cannot drop `required` or reach
// any other input attribute, and the spread still goes FIRST so the attributes below also win at
// runtime. A caller-supplied `aria-describedby` (the error) is prepended by the hint, not replaced by it.
export function AcceptTermsCheckbox({
  "aria-describedby": describedBy,
  ...props
}: Pick<ComponentProps<"input">, "ref" | "aria-invalid" | "aria-describedby">) {
  const t = useTranslations("pages");
  const hintId = useId();
  return (
    <div className="flex flex-col gap-1.5 border-t border-border pt-5">
      <label className="flex w-fit cursor-pointer items-start gap-2.5 py-3 text-label font-medium text-text-primary">
        <span className="relative inline-flex shrink-0 items-center justify-center pt-0.5">
          <input
            {...props}
            name="acceptTerms"
            type="checkbox"
            required
            aria-required="true"
            aria-describedby={describedBy ? `${hintId} ${describedBy}` : hintId}
            className="peer size-5 cursor-pointer appearance-none rounded-sm border-2 border-border-strong bg-surface-primary transition-colors duration-75 checked:border-brand-600 checked:bg-brand-600 aria-invalid:border-danger-600 aria-invalid:ring-3 aria-invalid:ring-destructive/20 disabled:cursor-not-allowed disabled:opacity-50"
          />
          <Check
            aria-hidden="true"
            strokeWidth={3}
            className="pointer-events-none absolute hidden size-3.5 text-white peer-checked:block"
          />
        </span>
        <span>
          {t.rich("auth.passwordless.consent.termsLabel", {
            terms: policyLink("/villkor"),
          })}
        </span>
      </label>
      <p className="text-body-sm text-text-primary">
        {t.rich("auth.passwordless.consent.privacySibling", {
          privacy: policyLink("/integritet"),
        })}
      </p>
      {/* text-text-primary (not -secondary) to match the sibling field hints in the same
          form and honour the high-contrast, no-muted-text copy rule. */}
      <p id={hintId} className="text-body-sm text-text-primary">
        {t("auth.passwordless.consent.termsHint")}
      </p>
    </div>
  );
}
