import { useEffect, useRef, useState, type ReactNode, type Ref } from "react";
import { REGEXP_ONLY_DIGITS } from "input-otp";
import { InputOTP, InputOTPGroup, InputOTPSlot } from "@/components/ui/input-otp";

const CODE_LENGTH = 6;
const SLOTS = Array.from({ length: CODE_LENGTH }, (_, index) => index);

// With JavaScript off the slots never fill, so the real input is drawn as a plain field. The
// library's own fallback hard-codes black and white and follows the system's dark mode.
const NO_SCRIPT_CSS =
  "[data-input-otp]{background-color:var(--jp-surface-primary)!important;color:var(--jp-text-primary)!important;caret-color:var(--jp-text-primary)!important;border:1px solid var(--jp-border-input)!important;border-radius:var(--jp-r-md)!important;letter-spacing:.25em!important;text-align:center!important;width:100%!important}";

// A code copied out of a mail can carry whitespace, and a pasted value that fails the digit pattern
// is refused whole.
const stripWhitespace = (text: string) => text.replace(/\s/g, "");

// The six-digit code field, in the shape ADR 0142's "Page form" binds as amended for #1826: ONE real
// input carrying the label, the value and the one tab stop, with six boxes drawn over it; a visible
// label, `one-time-code` so the platform's autofill fills it, numeric input mode, no placeholder. Every
// code the product asks for is typed here.
//
// The ids are props: a page can hold two code fields at once (a dialog over a card that already shows
// one), and two elements must never share an id.
export function CodeField({
  id,
  hintId,
  label,
  hint,
  invalid,
  errorId,
  leadingDescriptionId,
  inputRef,
  name,
  value,
  onValueChange,
}: {
  id: string;
  hintId: string;
  label: string;
  hint: ReactNode;
  /** A line read before the hint, e.g. the step's "we have sent a code to …". */
  leadingDescriptionId?: string;
  /** The field carries a message the user can correct; `errorId` names it. */
  invalid: boolean;
  errorId: string;
  inputRef?: Ref<HTMLInputElement>;
  /** For a `<form action>` that reads the code from `FormData`. */
  name?: string;
  value?: string;
  onValueChange?: (value: string) => void;
}) {
  const rootRef = useRef<HTMLDivElement>(null);
  const [typed, setTyped] = useState("");
  const controlled = value !== undefined;

  // An uncontrolled field empties with its form's reset, as the native input it replaces does: React
  // resets a `<form action>` once the action has run, and the library keeps a value of its own.
  useEffect(() => {
    if (controlled) return;
    const form = rootRef.current?.closest("form");
    if (!form) return;
    const clear = () => setTyped("");
    form.addEventListener("reset", clear);
    return () => form.removeEventListener("reset", clear);
  }, [controlled]);

  return (
    <div ref={rootRef} className="flex flex-col gap-1.5">
      <label htmlFor={id} className="text-label font-medium text-text-primary">
        {label}
      </label>
      <InputOTP
        ref={inputRef}
        id={id}
        name={name}
        maxLength={CODE_LENGTH}
        pattern={REGEXP_ONLY_DIGITS}
        inputMode="numeric"
        autoComplete="one-time-code"
        pasteTransformer={stripWhitespace}
        noScriptCSSFallback={NO_SCRIPT_CSS}
        required
        aria-required="true"
        aria-invalid={invalid ? true : undefined}
        aria-describedby={[leadingDescriptionId, hintId, invalid ? errorId : null]
          .filter(Boolean)
          .join(" ")}
        value={controlled ? value : typed}
        onChange={(next: string) => {
          if (!controlled) setTyped(next);
          onValueChange?.(next);
        }}
      >
        <InputOTPGroup aria-hidden="true">
          {SLOTS.map((index) => (
            <InputOTPSlot key={index} index={index} invalid={invalid} />
          ))}
        </InputOTPGroup>
      </InputOTP>
      <p id={hintId} className="text-body-sm text-text-primary">
        {hint}
      </p>
    </div>
  );
}
