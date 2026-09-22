import type { ChangeEventHandler, Ref } from "react";
import { Input } from "@/components/ui/input";

// The six-digit code field, in the one shape ADR 0142's "Page form" binds (design M3): ONE input,
// never six boxes, a visible label, `one-time-code` so the platform's autofill fills it, numeric input
// mode, no placeholder. Every code the product asks for is typed here, the login page's and the
// re-authentication's alike, so the shape cannot drift between them.
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
  inputRef,
  name,
  value,
  onChange,
}: {
  id: string;
  hintId: string;
  label: string;
  hint: string;
  /** The field carries a message the user can correct; `errorId` names it. */
  invalid: boolean;
  errorId: string;
  inputRef?: Ref<HTMLInputElement>;
  /** For a `<form action>` that reads the code from `FormData`. */
  name?: string;
  value?: string;
  onChange?: ChangeEventHandler<HTMLInputElement>;
}) {
  return (
    <div className="flex flex-col gap-1.5">
      <label htmlFor={id} className="text-label font-medium text-text-primary">
        {label}
      </label>
      <Input
        ref={inputRef}
        id={id}
        name={name}
        type="text"
        inputMode="numeric"
        autoComplete="one-time-code"
        pattern="[0-9]*"
        maxLength={6}
        required
        aria-required="true"
        aria-invalid={invalid ? true : undefined}
        aria-describedby={invalid ? `${hintId} ${errorId}` : hintId}
        value={value}
        onChange={onChange}
      />
      <p id={hintId} className="text-body-sm text-text-primary">
        {hint}
      </p>
    </div>
  );
}
