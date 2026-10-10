import { useTranslations } from "next-intl";
import { Info } from "lucide-react";
import { CvPreambleDisclosure } from "@/components/resumes/cv-preamble-disclosure";

/**
 * CvPreamble — neutral, display-only notice for the verbatim text a CV carried ABOVE its first
 * heading that no contact extractor claimed (#844, ADR 0109). RSC.
 *
 * ADR 0109's doctrine: the engine describes, the user classifies. We say what the text is — text
 * above the first heading, not classified — and NEVER claim it is a profile: no badge, no "Hittad i
 * filen", no grading, no prefill. Caller-gated: renders only when `preamble` is non-empty (null =
 * the residue was fully accounted for by name/e-mail/phone/location, the common case, and the case
 * that keeps A8's honest Fail alive).
 *
 * The text is folded away behind "Visa texten" (#2083). The toggle is a client island that holds
 * only the open state; the blockquote is rendered HERE and passed to it as children, so the CV text
 * is never a client prop.
 *
 * TWO CALLERS, TWO GUARANTEE CLASSES (#1060). The personnummer control differs by arm and the
 * difference is deliberate, so read it here rather than assume parity:
 *
 *  - STAGING (`/cv/granska/[parsedId]`, from `ParsedResumeDetailDto.Preamble`): pnr-redacted at
 *    the `GetParsedResume` mapper egress, in two layers. It has to be — a FLAGGED parse
 *    persists, since only promote is gated, so the read side is where suppression must happen.
 *  - CANONICAL (`/cv/[id]/granska`, from `ResumeContentDto.Preamble`): NOT read-redacted, by
 *    design. Canonical content is clean at the WRITE boundary — every `ResumeContent` in the
 *    product is built from a `ResumeContentDto`, and every write surface calls
 *    `ResumeContentPersonnummerGuard`, enforced by an architecture tripwire. See that DTO's
 *    docblock for why a second read-side redactor there would be worse, not safer.
 *
 * ADR 0109 Amendment (5c-b): the adopt/classify ACTION is FAS-DEFERRED. The review is read-only, so
 * the notice is display-only. The honest path to adopt the text is still to give it a heading in
 * the file and upload again.
 */
export function CvPreamble({ preamble }: { preamble: string | null }) {
  const t = useTranslations("resumes");
  const text = preamble?.trim();

  if (!text) return null;

  return (
    <div className="jp-cvnotice">
      <Info size={18} className="jp-cvnotice__icon" aria-hidden="true" />
      <p className="jp-cvnotice__text">
        <strong>{t("preamble.title")}</strong> {t("preamble.body")} {t("preamble.useHint")}
      </p>
      <CvPreambleDisclosure showLabel={t("preamble.show")} hideLabel={t("preamble.hide")}>
        {/* blockquote: the text is quoted verbatim from the user's own file. `pre-wrap` keeps the
            file's own line breaks. */}
        <blockquote className="jp-cvnotice__quote">{text}</blockquote>
      </CvPreambleDisclosure>
    </div>
  );
}
