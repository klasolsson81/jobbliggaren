"use client";

import { useEffect, useId, useRef, useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import { useFormatter, useTranslations } from "next-intl";
import { setResumeLanguageAction } from "@/lib/actions/resumes";
import type { ResumeLanguage } from "@/lib/dto/resumes";
import { formatTime } from "@/lib/i18n/format";
import { Segment, type SegmentOption } from "@/components/ui/segment";

/** The outcome of one write, owned by the control that started it (#1391). */
type WriteOutcome = { ok: true; at: Date } | { ok: false; error: string };

/**
 * The saved CV's language on its review page (#2353; Klas 2026-11-27, design-reviewer's ruling).
 * Direct-apply and optimistic: a change is saved at once and the review re-runs in that language,
 * and a refusal restores the previous choice. The options stay focusable while the save is in
 * flight (`Segment`'s pending mode). After a refusal, focus returns to the checked option, which
 * the message belongs to (the `language-setting.tsx` pattern).
 */
export function CvLanguageChoice({
  resumeId,
  initialLanguage,
}: {
  resumeId: string;
  initialLanguage: ResumeLanguage;
}) {
  const t = useTranslations("resumes.languageChoice");
  const format = useFormatter();
  const router = useRouter();
  const labelId = useId();
  const errorId = useId();
  const groupRef = useRef<HTMLDivElement>(null);

  const [language, setLanguage] = useState<ResumeLanguage>(initialLanguage);
  const [isPending, startTransition] = useTransition();
  const [outcome, setOutcome] = useState<WriteOutcome | null>(null);

  const error = outcome?.ok === false ? outcome.error : null;
  useEffect(() => {
    if (error === null || isPending) return;
    groupRef.current
      ?.querySelector<HTMLButtonElement>('[role="radiogroup"] button[aria-checked="true"]')
      ?.focus();
  }, [error, isPending]);

  const options: ReadonlyArray<SegmentOption<ResumeLanguage>> = [
    { value: "Sv", label: t("swedish") },
    { value: "En", label: t("english") },
  ];

  function onChange(next: ResumeLanguage) {
    if (isPending || next === language) return;
    const previous = language;
    setLanguage(next);
    setOutcome(null);
    startTransition(async () => {
      const result = await setResumeLanguageAction(resumeId, next);
      if (!result.success) {
        setLanguage(previous);
        setOutcome({ ok: false, error: result.error });
        return;
      }
      setOutcome({ ok: true, at: new Date() });
      router.refresh();
    });
  }

  const status = isPending
    ? t("saving")
    : outcome?.ok
      ? t("savedAt", { time: formatTime(format, outcome.at) })
      : "";

  return (
    <div ref={groupRef} className="jp-cvledger__language">
      <span id={labelId} className="jp-cvledger__language-label">
        {t("label")}
      </span>
      <Segment
        aria-labelledby={labelId}
        aria-describedby={error ? errorId : undefined}
        value={language}
        onChange={onChange}
        options={options}
        pending={isPending}
      />
      {/* Mutually exclusive live regions (#1391): a refusal is an assertive alert, otherwise a
          polite status that stays mounted. */}
      {error ? (
        <p id={errorId} role="alert" className="text-body-sm text-danger-600">
          {error}
        </p>
      ) : (
        <p role="status" aria-live="polite" className="text-body-sm text-text-secondary">
          {status}
        </p>
      )}
    </div>
  );
}
