import {
  BadgeCheck,
  Bell,
  Building2,
  CalendarCheck,
  Clock,
  Hourglass,
  MessageSquare,
  Search,
  type LucideIcon,
} from "lucide-react";
import type { NoticeType } from "./notice-types";

/**
 * One icon per notice TYPE for the two list cards on `/oversikt` (ADR 0140). A `Record` over
 * the SSOT union, so a new type without an icon is a compile error — the same construction as
 * the orchestrator's `prefLabels`. The two prepared types with no producer yet carry a
 * placeholder each so the record stays total.
 */
export const NOTICE_ICONS: Record<NoticeType, LucideIcon> = {
  followup: MessageSquare,
  interviews: CalendarCheck,
  offers: BadgeCheck,
  statuschanges: Bell,
  deadlines: Hourglass,
  matches: Search,
  latestsearch: Clock,
  followedads: Building2,
  companyevents: Building2,
};
