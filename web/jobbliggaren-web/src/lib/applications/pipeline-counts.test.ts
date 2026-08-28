import { describe, it, expect } from "vitest";
import type { PipelineGroupDto } from "@/lib/dto/applications";
import type { ApplicationStatus } from "@/lib/dto/applications";
import {
  activeCount,
  countByStatus,
  statusCount,
  totalCount,
} from "./pipeline-counts";

/**
 * ⚠ Fixturen bygger grupper som backend faktiskt producerar: GetPipeline gör
 * GroupBy(Status) och emitterar därför INGEN grupp för en tom status — aldrig
 * en grupp med noll. Ett test som zero-fyller fixturen hade vilat på en premiss
 * produktionen inte kan skapa (AGENTS.md §5 Tests) och hade dessutom inte kunnat
 * falla av det skäl det finns: hela poängen med statusCount är den utelämnade
 * statusen.
 */
function group(status: ApplicationStatus, count: number): PipelineGroupDto {
  return { status, count, applications: [] };
}

describe("countByStatus / statusCount", () => {
  it("ger 0 för en status som saknar grupp helt", () => {
    const counts = countByStatus([group("Submitted", 2)]);

    expect(statusCount(counts, "Submitted")).toBe(2);
    // Utelämnad av GroupBy, inte närvarande med noll.
    expect(counts.has("Draft")).toBe(false);
    expect(statusCount(counts, "Draft")).toBe(0);
  });

  it("ger 0 för varje status i en tom pipeline", () => {
    const counts = countByStatus([]);

    expect(totalCount(counts)).toBe(0);
    expect(activeCount(counts)).toBe(0);
    expect(statusCount(counts, "OfferReceived")).toBe(0);
  });
});

describe("totalCount", () => {
  it("räknar terminala statusar, inte bara aktiva", () => {
    const counts = countByStatus([group("Submitted", 2), group("Rejected", 3)]);

    expect(totalCount(counts)).toBe(5);
  });

  it("räknar ett konto som bara har avslutade ansökningar", () => {
    const counts = countByStatus([
      group("Rejected", 3),
      group("Accepted", 1),
      group("Withdrawn", 1),
      group("Ghosted", 2),
    ]);

    expect(totalCount(counts)).toBe(7);
    expect(activeCount(counts)).toBe(0);
  });
});

describe("activeCount", () => {
  it("summerar exakt de sex icke-terminala stegen", () => {
    const counts = countByStatus([
      group("Draft", 1),
      group("Submitted", 2),
      group("Acknowledged", 3),
      group("InterviewScheduled", 4),
      group("Interviewing", 5),
      group("OfferReceived", 6),
    ]);

    expect(activeCount(counts)).toBe(21);
    expect(totalCount(counts)).toBe(21);
  });

  it("räknar INTE Ghosted som aktiv", () => {
    // Repot bar tidigare en andra definition av "aktiv" (oversikt/aggregations)
    // som inkluderade Ghosted. Den här raden är pinnen mot att den återuppstår.
    const counts = countByStatus([group("Ghosted", 4)]);

    expect(activeCount(counts)).toBe(0);
    expect(totalCount(counts)).toBe(4);
  });
});
