import { useTranslations } from "next-intl";

/**
 * CvPreamble — neutral, display-only affordance for the verbatim text a CV carried ABOVE its
 * first heading that no contact extractor claimed (#844, ADR 0109). RSC.
 *
 * ADR 0109's doctrine: the engine describes, the user classifies. We show the text and say what
 * it is — text above the first heading, not classified — and NEVER claim it is a profile: no
 * badge, no "Hittad i filen", no grading, no prefill. Caller-gated: renders only when `preamble`
 * is non-empty (null = the residue was fully accounted for by name/e-mail/phone/location, the
 * common case, and the case that keeps A8's honest Fail alive).
 *
 * CV-PII: `preamble` is already pnr-redacted at the GetParsedResume mapper egress (parity with
 * GetResumeAtsText's belt-and-braces redaction). It is rendered here in the SERVER component
 * only — never hydrated into a client island (parity with the review page's parsed.content rule).
 *
 * ADR 0109 Amendment (5c-b): the adopt/classify ACTION is FAS-DEFERRED. The Slutför guide that
 * once hosted it is retired (ADR 0112) and the review is read-only, so the affordance is
 * display-only. The honest path to adopt the text is to give it a heading in the file and upload
 * again — the same residue blocks auto-promote (AutoPromoteBlockReason.UnclassifiedPreamble), so
 * this is precisely the explanation the pending outcome owes the user, never an in-app rewrite.
 */
export function CvPreamble({ preamble }: { preamble: string | null }) {
  const t = useTranslations("resumes");
  const text = preamble?.trim();

  if (!text) return null;

  return (
    <section aria-labelledby="cv-preamble-title" className="flex flex-col gap-3">
      <h2
        id="cv-preamble-title"
        className="text-h3 font-medium text-text-primary"
      >
        {t("preamble.title")}
      </h2>
      <p className="text-body-sm text-text-primary">{t("preamble.body")}</p>
      {/* blockquote: the text is quoted verbatim from the user's own file — semantic, not a
          nested landmark. whitespace-pre-wrap keeps the file's own line breaks. */}
      <blockquote className="m-0 whitespace-pre-wrap rounded-md border border-border bg-card p-4 text-body-sm text-text-primary">
        {text}
      </blockquote>
      <p className="text-body-sm text-text-primary">{t("preamble.useHint")}</p>
    </section>
  );
}
