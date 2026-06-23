"use client";

// "use client": a controlled numeric input that owns its onChange + parses the
// raw string to `number | null`. Lives in the same hosts as SkillSection
// (wizard step + settings). ADR 0079 STEG 3: a single profile-level
// "antal års erfarenhet" — stored and round-tripped, but NOT scored anywhere in
// the FE (no badge, no grade math). Label/help text carries the instruction
// (no placeholder example text — hard Klas rule).

import { useId } from "react";
import { useTranslations } from "next-intl";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";

const EXPERIENCE_YEARS_MIN = 0;
const EXPERIENCE_YEARS_MAX = 70;

interface ExperienceFieldProps {
  /** Current value (`null` = not stated — never 0, which means "stated zero"). */
  readonly value: number | null;
  /** Emits the parsed value: a clamped integer, or `null` when the field is empty. */
  readonly onChange: (next: number | null) => void;
  /** Unik DOM-id-prefix så fältet kan monteras i flera värdar utan id-kollision. */
  readonly idPrefix?: string;
}

/**
 * "Antal års erfarenhet" — EN frivillig profil-nivå-siffra (ADR 0079 STEG 3).
 * Tomt fält = `null` (ej angivet, ärligt). Inga exempel i fältet; label +
 * hjälptext bär instruktionen. Klampar till 0..70 (speglar schemat/backend).
 */
export function ExperienceField({
  value,
  onChange,
  idPrefix = "match-experience",
}: ExperienceFieldProps) {
  const t = useTranslations("settings");
  const reactId = useId();
  const fieldId = `${idPrefix}-${reactId}`;
  const hintId = `${fieldId}-hint`;

  function handleChange(raw: string) {
    const trimmed = raw.trim();
    if (trimmed === "") {
      onChange(null);
      return;
    }
    const parsed = Number.parseInt(trimmed, 10);
    if (!Number.isFinite(parsed)) {
      // Non-numeric input (the browser usually blocks it for type=number, but a
      // paste can slip through): treat as "not stated" rather than NaN.
      onChange(null);
      return;
    }
    const clamped = Math.min(
      EXPERIENCE_YEARS_MAX,
      Math.max(EXPERIENCE_YEARS_MIN, parsed)
    );
    onChange(clamped);
  }

  return (
    <div className="flex flex-col gap-1.5">
      <Label htmlFor={fieldId}>{t("matchPrefs.experience.label")}</Label>
      <Input
        id={fieldId}
        type="number"
        inputMode="numeric"
        min={EXPERIENCE_YEARS_MIN}
        max={EXPERIENCE_YEARS_MAX}
        step={1}
        value={value === null ? "" : String(value)}
        onChange={(e) => handleChange(e.target.value)}
        aria-describedby={hintId}
        className="max-w-[12rem]"
      />
      <p id={hintId} className="text-body-sm text-text-secondary">
        {t("matchPrefs.experience.hint")}
      </p>
    </div>
  );
}
