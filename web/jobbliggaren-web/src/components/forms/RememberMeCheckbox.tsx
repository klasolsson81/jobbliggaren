import { Check } from "lucide-react";

// "Håll mig inloggad" opt-in for the login form (PR2b-3b, epic #481).
//
// A NATIVE <input type="checkbox" name="rememberMe">: LoginForm is an
// uncontrolled `<form action={serverAction}>`, and the Server Action reads the
// value from FormData (a checked native checkbox posts "on", unchecked posts
// nothing). That rules out ToggleRow (a controlled `role="switch"` button that
// needs React state and never posts via FormData) and the `aria-checked` custom
// widgets (jp-checkitem/radio-group — also controlled, also non-form). The native
// input additionally gives free keyboard operation (Space), the global
// :focus-visible ring, and correct screen-reader semantics with zero JS.
//
// Unticked by default — a pre-ticked box is invalid consent (GDPR Art. 7).
// `defaultChecked` is not a way around that: React 19 resets this uncontrolled
// form after every action, silently unticking a box the user DID tick, and the
// login action echoes the submitted opt-in back so the form can re-apply it. The
// only value a caller can pass is the one that arrived in that submit's own
// FormData, so this restores a choice the user made and never invents one.
//
// The box is styled with design tokens to match .jp-checkitem__box: a 2px
// border-strong boundary (#7C8AA0, the WCAG 1.4.11 3:1 UI floor — NOT the lighter
// border-input which fails it), accent-800 fill + white check when checked. No
// custom CSS, no new dependency.

interface RememberMeCheckboxProps {
  label: string;
  defaultChecked?: boolean;
}

export function RememberMeCheckbox({
  label,
  defaultChecked = false,
}: RememberMeCheckboxProps) {
  return (
    // Implicit label wraps the control: one large, gap-free hit target
    // (min-h-11 = 44px, the touch floor + the form's input height) with no
    // double-toggle risk. w-fit keeps the target only as wide as its content.
    <label className="flex min-h-11 w-fit cursor-pointer items-center gap-2.5 text-label font-medium text-text-primary">
      <span className="relative inline-flex shrink-0 items-center justify-center">
        <input
          name="rememberMe"
          type="checkbox"
          defaultChecked={defaultChecked}
          className="peer size-5 cursor-pointer appearance-none rounded-sm border-2 border-border-strong bg-surface-primary transition-colors duration-75 checked:border-brand-600 checked:bg-brand-600 disabled:cursor-not-allowed disabled:opacity-50"
        />
        <Check
          aria-hidden="true"
          strokeWidth={3}
          className="pointer-events-none absolute hidden size-3.5 text-white peer-checked:block"
        />
      </span>
      {label}
    </label>
  );
}
