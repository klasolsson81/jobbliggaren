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
type PasswordInputProps = Omit<React.ComponentProps<"input">, "type">;

export function PasswordInput({ className, ...props }: PasswordInputProps) {
  const t = useTranslations("pages");
  const [visible, setVisible] = React.useState(false);

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
        aria-pressed={visible}
        aria-label={visible ? t("auth.hidePassword") : t("auth.showPassword")}
        // px-3.5 → 44px hit-target width (matches the 44px height; JobbPilot touch rule).
        className="absolute inset-y-0 right-0 flex items-center rounded-r-md px-3.5 text-text-secondary transition-colors duration-75 hover:text-text-primary focus-visible:text-text-primary focus-visible:outline-none focus-visible:ring-3 focus-visible:ring-ring/50"
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
