import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import { resetMyDataAction } from "@/lib/dev/reset-actions";

/**
 * DEV-ONLY — REMOVE BEFORE LAUNCH (Klas). An unobtrusive note at the bottom of
 * /oversikt that lets the developer wipe their own test data (CVs, saved/recent
 * searches, match preferences) and re-run the onboarding flow from scratch. The
 * caller MUST render this only outside production (`process.env.NODE_ENV !==
 * "production"`) — defense in depth alongside the backend endpoint being mapped
 * only in Development.
 */
export function ResetMyDataNote() {
  const t = useTranslations("common");
  return (
    <div className="mt-8 rounded-md border border-dashed border-border bg-muted/40 p-4 text-body-sm leading-5 text-text-secondary">
      <p className="mb-2">{t("dev.note")}</p>
      <form action={resetMyDataAction}>
        <Button type="submit" variant="outline" size="sm">
          {t("dev.resetButton")}
        </Button>
      </form>
    </div>
  );
}
