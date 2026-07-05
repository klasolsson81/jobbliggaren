import { Check } from "lucide-react";

// "Håll mig inloggad" opt-in for the auth forms (PR2b-3b, epic #481).
//
// A NATIVE <input type="checkbox" name="rememberMe">: LoginForm/RegisterForm are
// uncontrolled `<form action={serverAction}>`, and the Server Action reads the
// value from FormData (a checked native checkbox posts "on", unchecked posts
// nothing). That rules out ToggleRow (a controlled `role="switch"` button that
// needs React state and never posts via FormData) and the `aria-checked` custom
// widgets (jp-checkitem/radio-group — also controlled, also non-form). The native
// input additionally gives free keyboard operation (Space), the global
// :focus-visible ring, and correct screen-reader semantics with zero JS.
//
// Unticked by default — a pre-ticked box is invalid consent (GDPR Art. 7). The
// box is styled with design tokens to match .jp-checkitem__box: a 2px
// border-strong boundary (#7C8AA0, the WCAG 1.4.11 3:1 UI floor — NOT the lighter
// border-input which fails it), accent-800 fill + white check when checked. No
// custom CSS, no new dependency. `label` and `hint` are passed in so the single
// component serves both auth namespaces (auth.login.* / auth.register.*).

interface RememberMeCheckboxProps {
  label: string;
  hint: string;
}

export function RememberMeCheckbox({ label, hint }: RememberMeCheckboxProps) {
  return (
    <div className="flex flex-col gap-1.5">
      {/* Implicit label wraps the control: one large, gap-free hit target
          (min-h-11 = 44px, the touch floor + the form's input height) with no
          double-toggle risk. w-fit keeps the target only as wide as its content. */}
      <label className="flex min-h-11 w-fit cursor-pointer items-center gap-2.5 text-label font-medium text-text-primary">
        <span className="relative inline-flex shrink-0 items-center justify-center">
          <input
            name="rememberMe"
            type="checkbox"
            aria-describedby="rememberMe-hint"
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
      <p id="rememberMe-hint" className="text-body-sm text-text-secondary">
        {hint}
      </p>
    </div>
  );
}
