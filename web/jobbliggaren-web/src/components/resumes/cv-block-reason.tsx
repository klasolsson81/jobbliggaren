import { useTranslations } from "next-intl";
import { StatusPill } from "@/components/ui/status-pill";
import type { AutoPromoteBlockReason } from "@/lib/dto/parsed-resume";

/**
 * "Därför är filen inte sparad som CV" (#1060). RSC.
 *
 * Svaret på frågan användaren faktiskt har när hon klickar på hubbens åtgärdskort.
 * Före den här komponenten kunde hon inte få veta orsaken alls utan att ladda upp
 * filen på nytt (#1060 delkrav 3) — kortet sa bara att något behövde åtgärdas.
 *
 * Återanvänder `.jp-cvaction` med flit: det ÄR samma kort hon just klickade på, nu
 * med svaret i sig. Ingen ny CSS, inget nytt mönster, och kontinuiteten är avsiktlig.
 *
 * `reason === null` är inte ett tomt tillstånd utan ett eget besked. Ett utkast kan
 * ligga kvar pending medan grinden som stoppade det har pensionerats under tiden
 * (PR B pensionerade en grind och smalnade en till), och då är det ärliga svaret att
 * inget hindrar filen längre — inte tystnad.
 *
 * Copyn är en sluten mängd, en sträng per grind. `AutoPromoteBlockReason` är låst i
 * zod-schemat, så en ny backend-grind fail-loud:ar i DTO-parsningen i stället för att
 * rendera ett block utan text.
 */
export function CvBlockReason({
  reason,
}: {
  reason: AutoPromoteBlockReason | null;
}) {
  const t = useTranslations("pages.cv");

  const cleared = reason === null;
  const headingId = "cv-blockreason-title";

  return (
    <section aria-labelledby={headingId} className="jp-cvaction">
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
    </section>
  );
}
