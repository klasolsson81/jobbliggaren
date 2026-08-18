"use client";

import { useEffect, useId, useRef } from "react";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";

interface PersonalInfoCardProps {
  displayName: string;
  email: string;
  isPending: boolean;
  error: string | null;
  /** #1117: which input `error` belongs to, or null when it is not a field error. */
  errorField: "displayName" | null;
  savedAt: Date | null;
  onDisplayNameChange: (next: string) => void;
  onSubmit: (e: React.FormEvent<HTMLFormElement>) => void;
}

/**
 * Personuppgifter-kort. Innehåller Namn (write via displayName) + E-postadress
 * (read-only från session). "Spara ändringar" submitter formet via parent-
 * orchestrerad action. FAS-DEFERRAL (CTO 2026-05-20 Val 4B): Telefon-fält
 * ej renderat — DTO saknar `phone`-fält, no-mock-doktrin.
 */
export function PersonalInfoCard({
  displayName,
  email,
  isPending,
  error,
  errorField,
  savedAt,
  onDisplayNameChange,
  onSubmit,
}: PersonalInfoCardProps) {
  const t = useTranslations("settings");
  const errorId = useId();
  const nameRef = useRef<HTMLInputElement>(null);
  const nameInvalid = error !== null && errorField === "displayName";

  // Focus the input the message names, so a keyboard or screen-reader user lands on the
  // control to correct rather than only hearing that something is wrong.
  useEffect(() => {
    if (nameInvalid) nameRef.current?.focus();
  }, [nameInvalid, error]);
  return (
    <section className="jp-card">
      <h2 className="jp-card__title">{t("personalInfo.title")}</h2>
      <form onSubmit={onSubmit} className="flex flex-col gap-5">
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="settings-name">{t("personalInfo.nameLabel")}</Label>
          <Input
            ref={nameRef}
            id="settings-name"
            type="text"
            value={displayName}
            onChange={(e) => onDisplayNameChange(e.target.value)}
            maxLength={200}
            required
            disabled={isPending}
            autoComplete="name"
            aria-invalid={nameInvalid ? true : undefined}
            aria-describedby={nameInvalid ? errorId : undefined}
          />
        </div>
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="settings-email">{t("personalInfo.emailLabel")}</Label>
          <Input
            id="settings-email"
            type="email"
            value={email}
            readOnly
            aria-describedby="settings-email-hint"
          />
          <p
            id="settings-email-hint"
            className="text-body-sm text-text-primary"
          >
            {t("personalInfo.emailHint")}
          </p>
        </div>
        {error && (
          <p id={errorId} role="alert" className="text-body-sm text-danger-600">
            {error}
          </p>
        )}
        {savedAt && !error && (
          <p
            role="status"
            aria-live="polite"
            className="text-body-sm text-text-secondary"
          >
            {t("personalInfo.saved")}
          </p>
        )}
        <div>
          <Button type="submit" disabled={isPending}>
            {isPending ? t("personalInfo.saving") : t("personalInfo.save")}
          </Button>
        </div>
      </form>
    </section>
  );
}
