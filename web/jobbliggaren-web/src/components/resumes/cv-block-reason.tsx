import Link from "next/link";
import { useTranslations } from "next-intl";
import { StatusPill } from "@/components/ui/status-pill";
import { cn } from "@/lib/utils";
import type { AutoPromoteBlockReason } from "@/lib/dto/parsed-resume";

/**
 * "Därför är filen inte sparad som CV" (#1060). RSC.
 *
 * Svaret på frågan användaren faktiskt har när hon klickar på hubbens åtgärdskort.
 * Före den här komponenten kunde hon inte få veta orsaken alls utan att ladda upp
 * filen på nytt (#1060 delkrav 3) — kortet sa bara att något behövde åtgärdas.
 *
 * Återanvänder `.jp-cvaction`: det ÄR samma kort hon just klickade på, nu med svaret
 * i sig. Klarat-läget lägger till `--ok`, för basklassens vänsteraccent är låst till
 * `--jp-warning`, och en grön pill i ett orange kort är kortet som motsäger sitt eget
 * besked (design-reviewer). Inga nya tokens; båda finns i båda teman.
 *
 * **`reason === null` betyder "inget i FILEN hindrar den", aldrig "det här kommer att
 * sparas"** (CTO-bind D1). Grindens etikett-kanal läser CV-namnet från uppladdnings-
 * formuläret, som läsvägen inte har, så ett personnummer i det fältet är *ej bedömt*
 * här. Copyn är scopead till filen och skriver ut den obedömda kanalen i en mening;
 * den får inte intyga en sparning som inte har hänt (CLAUDE.md §5).
 *
 * Att rendera ingenting vore fel: ett utkast kan ligga kvar pending medan grinden som
 * stoppade det har pensionerats (PR B pensionerade en och smalnade en till), och för
 * de användarna är tystnad exakt den defekt #1060 filades för.
 *
 * `AutoPromoteBlockReason` är låst i zod-schemat, så en ny backend-grind fail-loud:ar i
 * DTO-parsningen i stället för att rendera ett block utan text.
 */
export function CvBlockReason({
  reason,
  className,
}: {
  reason: AutoPromoteBlockReason | null;
  /** Layout escape hatch for the host page. `.jp-cvaction` carries a 20px bottom margin that
   * is load-bearing on the hub, whose container has no gap; the review page is a gap-6 column
   * and passes `jp-cvaction--flush` rather than the base margin moving.
   *
   * It must be a MODIFIER, not a Tailwind utility: `.jp-cvaction` is unlayered and utilities
   * live in `@layer utilities`, so `mb-0` would be silently inert (DESIGN.md §141). My first
   * version of this line said it passed `mb-0` — a claim about an outcome that never happened. */
  className?: string;
}) {
  const t = useTranslations("pages.cv");

  const cleared = reason === null;
  const headingId = "cv-blockreason-title";

  return (
    <section
      aria-labelledby={headingId}
      className={cn(
        "jp-cvaction",
        cleared && "jp-cvaction--ok",
        className,
      )}
    >
      <StatusPill tone={cleared ? "success" : "warning"}>
        {cleared ? t("review.blockReason.clearedKicker") : t("pending.kicker")}
      </StatusPill>
      <div className="jp-cvaction__lead">
        <h2 id={headingId} className="jp-cvaction__heading">
          {cleared
            ? t("review.blockReason.clearedTitle")
            : t("review.blockReason.title")}
        </h2>
        <p className="jp-cvaction__body">
          {cleared
            ? t("review.blockReason.clearedBody")
            : t(`review.blockReason.${reason}`)}
        </p>
      </div>

      {/* Varje tillstånd som ger en instruktion får kontrollen bredvid instruktionen
          (ADR 0047). Personnumret i visningsnamnet ändras under Inställningar, tre
          skärmar bort och namngivet ingen annanstans; det klarade läget pekar på
          uppladdningen, som annars bara finns längst ned på sidan. */}
      {reason === "PersonnummerInAccountName" && (
        <div className="jp-cvaction__actions">
          <Link href="/installningar" className="jp-btn jp-btn--secondary">
            {t("review.blockReason.settingsCta")}
          </Link>
        </div>
      )}
      {cleared && (
        <div className="jp-cvaction__actions">
          <Link href="/cv/importera" className="jp-btn jp-btn--secondary">
            {t("review.nextStepCta")}
          </Link>
        </div>
      )}
    </section>
  );
}
