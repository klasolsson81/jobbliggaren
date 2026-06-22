"use client";

import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";

interface PersonalInfoCardProps {
  displayName: string;
  email: string;
  isPending: boolean;
  error: string | null;
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
  savedAt,
  onDisplayNameChange,
  onSubmit,
}: PersonalInfoCardProps) {
  const t = useTranslations("settings");
  return (
    <section className="jp-card">
      <h2 className="jp-card__title">{t("personalInfo.title")}</h2>
      <form onSubmit={onSubmit} className="flex flex-col gap-5">
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="settings-name">{t("personalInfo.nameLabel")}</Label>
          <Input
            id="settings-name"
            type="text"
            value={displayName}
            onChange={(e) => onDisplayNameChange(e.target.value)}
            maxLength={200}
            required
            disabled={isPending}
            autoComplete="name"
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
            className="text-body-sm text-text-secondary"
          >
            {t("personalInfo.emailHint")}
          </p>
        </div>
        {error && (
          <p role="alert" className="text-body-sm text-danger-600">
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
