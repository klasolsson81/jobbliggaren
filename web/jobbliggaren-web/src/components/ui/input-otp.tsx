"use client"

import * as React from "react"
import { OTPInput, OTPInputContext } from "input-otp"

import { cn } from "@/lib/utils"

function InputOTP({
  className,
  containerClassName,
  ...props
}: React.ComponentProps<typeof OTPInput> & {
  containerClassName?: string
}) {
  return (
    <OTPInput
      data-slot="input-otp"
      containerClassName={cn("flex w-fit items-center", containerClassName)}
      spellCheck={false}
      className={className}
      {...props}
    />
  )
}

function InputOTPGroup({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="input-otp-group"
      className={cn("flex items-center", className)}
      {...props}
    />
  )
}

function InputOTPSlot({
  index,
  invalid,
  className,
  ...props
}: React.ComponentProps<"div"> & {
  index: number
  invalid?: boolean
}) {
  const inputOTPContext = React.useContext(OTPInputContext)
  const { char, hasFakeCaret, isActive } = inputOTPContext?.slots[index] ?? {}

  return (
    <div
      data-slot="input-otp-slot"
      data-active={isActive}
      className={cn(
        "relative flex size-11 items-center justify-center border-y border-r border-border-input bg-surface-primary text-h2 font-semibold text-text-primary tabular-nums transition-colors duration-75 first:rounded-l-md first:border-l last:rounded-r-md data-[active=true]:z-10 data-[active=true]:outline-2 data-[active=true]:outline-offset-2 data-[active=true]:outline-(--jp-focus)",
        invalid && "border-destructive",
        className
      )}
      {...props}
    >
      {char}
      {hasFakeCaret && (
        <div className="pointer-events-none absolute inset-0 flex items-center justify-center">
          <div className="h-5 w-px animate-caret-blink bg-text-primary motion-reduce:animate-none" />
        </div>
      )}
    </div>
  )
}

export { InputOTP, InputOTPGroup, InputOTPSlot }
