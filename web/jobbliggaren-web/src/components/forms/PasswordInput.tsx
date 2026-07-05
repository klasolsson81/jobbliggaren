"use client";

import * as React from "react";
import { Eye, EyeOff } from "lucide-react";
import { useTranslations } from "next-intl";
import { Input } from "@/components/ui/input";
import { cn } from "@/lib/utils";

// #586 — password field with a standard show/hide toggle. Masked by default;
// the eye button reveals the value so the user can check for typos. Pure client
// UI: the value submits normally (the toggle never touches the payload). Reused
// by LoginForm + RegisterForm so both fields behave identically (DRY).
// #613 — `disabled` reaches the toggle too, so the field cannot be revealed while
// the form is submitting (e.g. the account-deletion dialog under isPending).
// #678 — `fieldName` disambiguates the toggle's accessible name when a form has more
// than one password field (change-password: current/new/confirm). Omitted → the bare
// "Visa lösenord" (unchanged for Login/Register/delete, which have at most one field).
type PasswordInputProps = Omit<React.ComponentProps<"input">, "type"> & {
  fieldName?: string;
};

export function PasswordInput({ className, fieldName, ...props }: PasswordInputProps) {
  const t = useTranslations("pages");
  const [visible, setVisible] = React.useState(false);

  // With a fieldName the toggle reads "Visa {fieldName}" / "Dölj {fieldName}" so a
  // screen-reader user can tell multiple toggles apart (WCAG 2.4.6 / 4.1.2).
  const toggleLabel = visible
    ? fieldName
      ? t("auth.hidePasswordNamed", { field: fieldName })
      : t("auth.hidePassword")
    : fieldName
      ? t("auth.showPasswordNamed", { field: fieldName })
      : t("auth.showPassword");

  return (
    <div className="relative">
      <Input
        {...props}
        type={visible ? "text" : "password"}
        // Room for the toggle button so revealed text never slides under it.
        className={cn("pr-11", className)}
      />
      <button
        type="button"
        onClick={() => setVisible((v) => !v)}
        disabled={props.disabled}
        aria-pressed={visible}
        aria-label={toggleLabel}
        // px-3.5 → 44px hit-target width (matches the 44px height; JobbPilot touch rule).
        className="absolute inset-y-0 right-0 flex items-center rounded-r-md px-3.5 text-text-secondary transition-colors duration-75 hover:text-text-primary focus-visible:text-text-primary focus-visible:outline-none focus-visible:ring-3 focus-visible:ring-ring/50 disabled:cursor-not-allowed disabled:opacity-50 disabled:hover:text-text-secondary"
      >
        {visible ? (
          <EyeOff className="size-4" aria-hidden="true" />
        ) : (
          <Eye className="size-4" aria-hidden="true" />
        )}
      </button>
    </div>
  );
}
