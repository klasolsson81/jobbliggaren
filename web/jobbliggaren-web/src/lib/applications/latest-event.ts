import type { ApplicationDto, ApplicationStatus } from "@/lib/dto/applications";

/**
 * "Senaste händelse"-härledning för Tabell-vyn (#630 PR 10, design §7).
 *
 * FE-skalär (senior-cto-advisor-bind 2026-07-10 + Klas GO): den senaste
 * händelsen härleds PURT ur skalärer som redan finns i list-DTO:n — INGEN
 * backend-projektion, ingen ny query-yta. Nyckel-invarianten (verifierad i
 * `Application.TransitionTo`, single-writer `AppendStatusChange`): den nyaste
 * `StatusChange.To` == `Status` och dess `ChangedAt` == `LastStatusChangeAt`.
 * Alltså är "senaste statusbyte" fullt känt ur `status` + `lastStatusChangeAt`.
 * Tidslinje-collectionen förblir detail-only (ADR 0092 D4) — list-DTO:n rörs
 * inte. Uppföljningens fria notering är PII (DEK-gräns) och ytas ALDRIG i listan;
 * en uppföljningsrad visar generiskt "Uppföljning loggad".
 *
 * Data-grundad, aldrig fabricerad (§5 / ADR 0071): `createdAt` är alltid satt i
 * list-DTO:n, så en händelse kan alltid härledas.
 */
export type LatestEvent =
  | { kind: "StatusReached"; at: string; toStatus: ApplicationStatus }
  | { kind: "FollowUpLogged"; at: string };

export function latestEventOf(
  application: Pick<
    ApplicationDto,
    "status" | "createdAt" | "lastStatusChangeAt" | "lastFollowUpAt"
  >,
): LatestEvent {
  // Senaste statusbyte: `lastStatusChangeAt` (nyaste StatusChange, == nuvarande
  // status per invarianten ovan), annars `createdAt` (en oförändrad rad — t.ex.
  // ett nyskapat utkast — vars "senaste händelse" är skapandet; `createdAt` är
  // aldrig senare än ett statusbyte).
  const statusAt = application.lastStatusChangeAt ?? application.createdAt;
  const followUpAt = application.lastFollowUpAt;

  if (
    followUpAt != null &&
    new Date(followUpAt).getTime() > new Date(statusAt).getTime()
  ) {
    return { kind: "FollowUpLogged", at: followUpAt };
  }
  return { kind: "StatusReached", at: statusAt, toStatus: application.status };
}
