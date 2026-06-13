import {
  Calendar,
  Check,
  Clock,
  Inbox,
  Pencil,
  Send,
  Star,
  User,
  X,
  type LucideIcon,
} from "lucide-react";
import type { ApplicationStatus } from "@/lib/types/applications";

/**
 * Status → lucide-ikon. Speglar v3-prototypens ApplicationRow-mappning
 * (pages.jsx rad 233-244) men mot REAL ApplicationStatus-domänen (Draft
 * använder Pencil; v3 I.Edit ≡ lucide Pencil). Ren data-modul, ingen
 * "use client" — delas av app-row v3 (.jp-app__statusbadge) och
 * ansökan-modalens status-block så ikonvalet aldrig drifter (DRY,
 * CLAUDE.md §9.1).
 */
const STATUS_ICON: Record<ApplicationStatus, LucideIcon> = {
  Draft: Pencil,
  Submitted: Send,
  Acknowledged: Check,
  InterviewScheduled: Calendar,
  Interviewing: User,
  OfferReceived: Star,
  Accepted: Check,
  Rejected: X,
  Withdrawn: X,
  Ghosted: Clock,
};

export function getStatusIcon(status: ApplicationStatus): LucideIcon {
  return STATUS_ICON[status] ?? Inbox;
}

/**
 * Renderar status-ikonen via en modul-scope-komponent. Undviker
 * react-hooks/static-components ("Cannot create components during render")
 * som triggas av att alias:a en LucideIcon till en lokal versal variabel
 * och rendera den i samma render-pass. Här är komponent-identiteten stabil
 * (modul-scope) och bara `status`-propen växlar.
 */
export function StatusIcon({
  status,
  size,
}: {
  status: ApplicationStatus;
  size?: number;
}) {
  const Icon = STATUS_ICON[status] ?? Inbox;
  return <Icon size={size} aria-hidden="true" />;
}
