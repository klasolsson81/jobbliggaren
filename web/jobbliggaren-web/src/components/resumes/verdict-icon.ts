import { CircleAlert, CircleCheck, CircleDashed, CircleX } from "lucide-react";
import type { LucideIcon } from "lucide-react";
import type { CriterionVerdict } from "@/lib/dto/parsed-resume";

/** The review ledger's icon per verdict (#2083): the row's status and the strip's counts. */
export const VERDICT_ICON: Record<CriterionVerdict, LucideIcon> = {
  Fail: CircleX,
  Warn: CircleAlert,
  Pass: CircleCheck,
  NotAssessed: CircleDashed,
};
