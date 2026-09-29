"use client";

// "use client": a controlled numeric input that owns its onChange + parses the
// raw string to `number | null`. ADR 0079 STEG 3: a single profile-level
// "antal års erfarenhet" — stored and round-tripped, but NOT scored anywhere in
// the FE (no badge, no grade math). No placeholder example text (hard Klas rule).

import type { Ref } from "react";
import { Input } from "@/components/ui/input";

const EXPERIENCE_YEARS_MIN = 0;
const EXPERIENCE_YEARS_MAX = 70;

interface ExperienceFieldProps {
  /** Current value (`null` = not stated — never 0, which means "stated zero"). */
  readonly value: number | null;
  /** Emits the parsed value: a clamped integer, or `null` when the field is empty. */
  readonly onChange: (next: number | null) => void;
  /** The id of the element that names the field: its dialog's title, so no label repeats it. */
  readonly labelledBy: string;
  readonly inputRef?: Ref<HTMLInputElement>;
}

/**
 * "Antal års erfarenhet" — EN frivillig profil-nivå-siffra (ADR 0079 STEG 3).
 * Tomt fält = `null` (ej angivet, ärligt). Klampar till 0..70 (speglar schemat/backend).
 */
export function ExperienceField({ value, onChange, labelledBy, inputRef }: ExperienceFieldProps) {
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
    <Input
      ref={inputRef}
      type="number"
      inputMode="numeric"
      min={EXPERIENCE_YEARS_MIN}
      max={EXPERIENCE_YEARS_MAX}
      step={1}
      aria-labelledby={labelledBy}
      value={value === null ? "" : String(value)}
      onChange={(e) => handleChange(e.target.value)}
      className="max-w-[12rem]"
    />
  );
}
