"use client";

import { useEffect, useId, useMemo, useRef, useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import { useFormatter, useTranslations } from "next-intl";
import { setLocaleAction } from "@/i18n/set-locale-action";
import { makeUpdateMyProfileSchema } from "@/lib/actions/me-schemas";
import { updateMyProfileAction } from "@/lib/actions/me";
import { formatTime } from "@/lib/i18n/format";
import { Segment, type SegmentOption } from "@/components/ui/segment";

type LanguageValue = "sv" | "en";

/** The outcome of one write, owned by the control that started it (#1391). */
type WriteOutcome = { ok: true; at: Date } | { ok: false; error: string };

/**
 * The language on /mina-sidor/konto (#1891; before it the Visning card). Direct-apply: every change
 * is saved to the profile at once, optimistic, and reverted on failure.
 *
 * The UI locale flips only after the profile save succeeds. The cookie is the rendering source of
 * truth (ADR 0078) and the profile its durable backup, so writing the cookie unconditionally would
 * let it win permanently on a save failure.
 */
export function LanguageSetting({ initialLanguage }: { initialLanguage: string }) {
  const t = useTranslations("settings");
  const tv = useTranslations("validation");
  const format = useFormatter();
  const router = useRouter();
  const schema = useMemo(() => makeUpdateMyProfileSchema(tv), [tv]);
  const errorId = useId();
  const groupRef = useRef<HTMLDivElement>(null);

  const [language, setLanguage] = useState<LanguageValue>(
    initialLanguage === "en" ? "en" : "sv",
  );
  const [isPending, startTransition] = useTransition();
  const [outcome, setOutcome] = useState<WriteOutcome | null>(null);

  // The segment is `disabled` while the save is pending, which drops focus to <body>, and `Segment`'s
  // own restore effect is gated on the group already holding focus. So once the save settles, focus
  // goes back to the checked option: after a refusal always, since the message belongs to it, and
  // after a receipt only if focus was lost. `isPending` is in the guard: `focus()` on a disabled
  // element does nothing.
  const error = outcome?.ok === false ? outcome.error : null;
  useEffect(() => {
    if (outcome === null || isPending) return;
    const active = document.activeElement;
    if (outcome.ok && active !== null && active !== document.body) return;
    groupRef.current
      ?.querySelector<HTMLButtonElement>('[role="radiogroup"] button[aria-checked="true"]')
      ?.focus();
  }, [outcome, isPending]);

  const options: ReadonlyArray<SegmentOption<LanguageValue>> = [
    { value: "sv", label: t("display.languageSwedish") },
    { value: "en", label: t("display.languageEnglish") },
  ];

  function onChange(next: LanguageValue) {
    const previous = language;
    setLanguage(next);
    const parsed = schema.safeParse({ language: next });
    if (!parsed.success) {
      setOutcome({
        ok: false,
        error: parsed.error.issues[0]?.message ?? t("account.invalidInput"),
      });
      setLanguage(previous);
      return;
    }
    setOutcome(null);
    startTransition(async () => {
      const result = await updateMyProfileAction(parsed.data);
      if (!result.success) {
        setOutcome({ ok: false, error: result.error });
        setLanguage(previous);
        return;
      }
      setOutcome({ ok: true, at: new Date() });
      await setLocaleAction(next);
      router.refresh();
    });
  }

  return (
    <div ref={groupRef} className="jp-settings-field">
      <Segment
        aria-label={t("display.languageLabel")}
        aria-describedby={error ? errorId : undefined}
        value={language}
        onChange={onChange}
        options={options}
        disabled={isPending}
      />
      {/* Mutually exclusive live regions (#1391): a refusal is an assertive alert, otherwise a
          polite receipt that stays mounted. */}
      {error ? (
        <p id={errorId} role="alert" className="text-body-sm text-danger-600">
          {error}
        </p>
      ) : (
        <p role="status" aria-live="polite" className="text-body-sm text-text-secondary">
          {outcome?.ok ? t("savedAt", { time: formatTime(format, outcome.at) }) : ""}
        </p>
      )}
    </div>
  );
}
