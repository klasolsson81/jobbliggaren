import { ShieldAlert } from "lucide-react";
import type { PersonnummerScanDto } from "@/lib/dto/parsed-resume";

/**
 * Personnummer-varning (F4-9, ADR 0074 Invariant 1 + IMY flag-only). RSC.
 * Visas ENDAST när `found` är sant (caller-gatad). Civic varnings-ton (skild
 * från brand-bannern). Flag-only och icke-blockerande: ingen auto-borttagning,
 * användaren går vidare själv. Råvärdet finns ALDRIG i DTO:n — bara antal +
 * typer — så det kan aldrig ytas av misstag.
 */
export function PersonnummerWarning({
  personnummer,
}: {
  personnummer: PersonnummerScanDto;
}) {
  if (!personnummer.found) return null;

  return (
    <section
      role="region"
      aria-label="Personnummer hittat i ditt CV"
      className="jp-pnr-warning"
    >
      <span className="jp-pnr-warning__icon" aria-hidden="true">
        <ShieldAlert size={20} />
      </span>
      <div className="jp-pnr-warning__text">
        <h2 className="jp-pnr-warning__title">Ta bort personnummer</h2>
        <p className="jp-pnr-warning__body">
          Vi hittade {personnummer.count}{" "}
          {personnummer.count === 1 ? "förekomst" : "förekomster"} av det som
          ser ut som personnummer. Personnummer behövs inte i ett CV. Ta bort
          det innan du sparar.
        </p>
      </div>
    </section>
  );
}
