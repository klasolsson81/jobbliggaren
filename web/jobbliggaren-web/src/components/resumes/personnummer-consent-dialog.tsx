"use client";

import Link from "next/link";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";

/**
 * Personnummer-samtyckesdialog (CV-pivot 5c, ADR 0114 / 5b security-bind B6). JURIDISKT
 * BÄRANDE — versionerad copy: dialogens materiella villkor ÄR version "1", samma värde som
 * backendens `PnrConsentDialog.Version` (Application-konstanten som stämplas på
 * `ResumeFile.PnrConsentDialogVersion`; servern stämplar — versionen korsar aldrig wiren,
 * ADR 0114 §D4, så FE bär ingen egen konstant). När copyn (resumes.consent.* i sv.json)
 * ändras materiellt (villkoren, inte en stavfix) MÅSTE backend-konstanten bumpas i samma
 * PR — stämpeln ska namnge exakt den text användaren godkände.
 *
 * B6-kraven copyn uppfyller: (i) den NAMNGER fyndet utan att rendera värdet (bara att ett
 * personnummer hittades) · (ii) den säger exakt vad som samtyckes till — varaktig,
 * krypterad, ägar-endast lagring av ORIGINALFILEN som innehåller personnumret · (iii) ett
 * DISTINKT aktivt opt-in ("Spara filen ändå" — aldrig förikryssat, aldrig härlett ur
 * uppladdningen) · (v) svensk civic-ton, "du", inga utropstecken. Den sätter också ADR
 * 0114:s icke-triviala förväntan: att spara filen gör den INTE till ett användbart CV —
 * innehållet befordras aldrig med ett personnummer kvar (grind A/B/C lyfts aldrig).
 */
interface PersonnummerConsentDialogProps {
  open: boolean;
  /** The count from the PII-free finding — copy names it, never the value. */
  count: number;
  /** Saving = the acknowledge re-POST is in flight. */
  saving: boolean;
  /** Distinct affirmative act: the user consents to durable storage of the original file. */
  onConfirm: () => void;
  /** Decline: do not store the original file. The dialog closes; the caller proceeds. */
  onDecline: () => void;
}

export function PersonnummerConsentDialog({
  open,
  count,
  saving,
  onConfirm,
  onDecline,
}: PersonnummerConsentDialogProps) {
  const t = useTranslations("resumes.consent");

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        // Escape / scrim-click = decline (never a silent consent). A distinct affirmative
        // act is the ONLY path that stores the file (B6 iii).
        if (!next && !saving) onDecline();
      }}
    >
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("title")}</DialogTitle>
          <DialogDescription>{t("finding", { count })}</DialogDescription>
        </DialogHeader>

        {/* The second paragraph carries the consent terms (what is stored, that it is
            encrypted + owner-only, and that storage is not promotion). Kept out of
            DialogDescription so the aria-describedby stays a single, primary summary. */}
        <p className="text-body-sm text-text-primary">{t("storeExplainer")}</p>

        <p className="text-body-sm">
          <Link href="/integritet" className="text-brand-600 hover:underline">
            {t("privacyLink")}
          </Link>
        </p>

        <DialogFooter>
          {/* Decline är secondary (inte ghost): security-auditorns valfria härdning +
              design-reviewerns minor 1 — avböj-vägen ska vara visuellt fullvärdig
              bredvid samtyckesknappen (Art. 25(2)-hygien; bindande verdikt var
              ACCEPTABLE-AS-IS, detta är hårdare än kravet). */}
          <Button
            type="button"
            variant="secondary"
            size="sm"
            disabled={saving}
            onClick={onDecline}
          >
            {t("decline")}
          </Button>
          <Button
            type="button"
            variant="default"
            size="sm"
            disabled={saving}
            onClick={onConfirm}
          >
            {saving ? t("saving") : t("save")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
