"use client";

import * as React from "react";
import { RadioGroup as RadioGroupPrimitive } from "radix-ui";

import { cn } from "@/lib/utils";

/**
 * Radio-group (Radix-primitiv, civic-utility-tokenstil). Roving tabindex,
 * pilnavigering och role="radiogroup" kommer från Radix. Ingen ny
 * design-token: brand-600 prick, border-border-default ram, global
 * *:focus-visible-ring (sätts ej per komponent). Inga shadows/gradienter.
 */
function RadioGroup({
  className,
  ...props
}: React.ComponentProps<typeof RadioGroupPrimitive.Root>) {
  return (
    <RadioGroupPrimitive.Root
      data-slot="radio-group"
      className={cn("flex flex-col gap-2", className)}
      {...props}
    />
  );
}

function RadioGroupItem({
  className,
  children,
  id,
  ...props
}: React.ComponentProps<typeof RadioGroupPrimitive.Item>) {
  return (
    <label
      htmlFor={id}
      className={cn(
        "flex cursor-pointer items-center gap-3 rounded-md border border-border-default px-3 py-2.5",
        "transition-colors duration-75 hover:bg-surface-tertiary",
        "has-[:disabled]:cursor-not-allowed has-[:disabled]:opacity-50 has-[:disabled]:hover:bg-transparent",
        "has-[[data-state=checked]]:border-border-strong has-[[data-state=checked]]:bg-brand-50",
        className
      )}
    >
      <RadioGroupPrimitive.Item
        id={id}
        data-slot="radio-group-item"
        className={cn(
          "flex size-4 shrink-0 items-center justify-center rounded-pill border border-border-strong",
          "bg-surface-primary outline-none",
          "data-[state=checked]:border-brand-600",
          "disabled:cursor-not-allowed"
        )}
        {...props}
      >
        <RadioGroupPrimitive.Indicator className="flex items-center justify-center">
          <span
            aria-hidden="true"
            className="size-2 rounded-pill bg-brand-600"
          />
        </RadioGroupPrimitive.Indicator>
      </RadioGroupPrimitive.Item>
      <span className="text-body text-text-primary">{children}</span>
    </label>
  );
}

export { RadioGroup, RadioGroupItem };
