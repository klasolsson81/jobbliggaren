import { z } from "zod";
import type {
  AdminJuniorChoice,
  AdminJuniorDecision,
  AdminJuniorJobsPage,
  AdminJuniorTab,
  AdminJuniorVerdict,
} from "@/lib/admin/junior-jobs";
import { readableInstantSchema as instant } from "./_helpers";

/**
 * The admin junior queue's wire shapes (#2264, ADR 0187). The backend names its enums the .NET way; this file is
 * the one place they become the view model's. Every object is read strictly: a field the queue does not know is a
 * contract change, and the page says it could not read the queue rather than guess what the field means.
 */

export const JUNIOR_WIRE_VIEWS = ["Unclear", "Shown", "Decided"] as const;
export type JuniorWireView = (typeof JUNIOR_WIRE_VIEWS)[number];

const verdictSchema = z.enum(["Junior", "NotJunior", "Unclear"]);
const decisionSchema = z.enum(["Junior", "NotJunior"]);
const count = z.number().int().nonnegative();

const itemSchema = z.strictObject({
  id: z.guid(),
  title: z.string(),
  companyName: z.string(),
  municipalityName: z.string().nullable(),
  publishedAt: instant,
  expiresAt: instant.nullable(),
  verdict: verdictSchema,
  ruleId: z.string().min(1),
  decision: decisionSchema.nullable(),
});

/** `GET /api/v1/admin/junior-job-ads?view=&page=` → 200. */
export const juniorJobAdQueueSchema = z.strictObject({
  items: z.strictObject({
    items: z.array(itemSchema),
    totalCount: count,
    page: z.number().int().positive(),
    pageSize: z.number().int().positive(),
    totalPages: count,
  }),
  counts: z.strictObject({
    unclear: count,
    shown: count,
    decided: count,
  }),
});

export type JuniorJobAdQueueDto = z.infer<typeof juniorJobAdQueueSchema>;
export type JuniorWireDecision = z.infer<typeof decisionSchema>;

const WIRE_VIEW: Readonly<Record<AdminJuniorTab, JuniorWireView>> = {
  unclear: "Unclear",
  shown: "Shown",
  decided: "Decided",
};

/** The tab the backend names, for the list's `view`. */
export function wireJuniorView(tab: AdminJuniorTab): JuniorWireView {
  return WIRE_VIEW[tab];
}

const WIRE_DECISION: Readonly<Record<AdminJuniorDecision, JuniorWireDecision>> = {
  junior: "Junior",
  notJunior: "NotJunior",
};

/** The decision the backend names; null hands the ad back to the rules. */
export function wireJuniorDecision(choice: AdminJuniorChoice): JuniorWireDecision | null {
  return choice === null ? null : WIRE_DECISION[choice];
}

const VERDICT: Readonly<Record<z.infer<typeof verdictSchema>, AdminJuniorVerdict>> = {
  Junior: "junior",
  NotJunior: "notJunior",
  Unclear: "unclear",
};

const DECISION: Readonly<Record<JuniorWireDecision, AdminJuniorDecision>> = {
  Junior: "junior",
  NotJunior: "notJunior",
};

export function toJuniorJobsPage(dto: JuniorJobAdQueueDto): AdminJuniorJobsPage {
  const { items, counts } = dto;
  return {
    items: items.items.map((item) => ({
      id: item.id,
      title: item.title,
      company: item.companyName,
      municipality: item.municipalityName,
      publishedAt: item.publishedAt,
      expiresAt: item.expiresAt,
      verdict: VERDICT[item.verdict],
      ruleId: item.ruleId,
      decision: item.decision === null ? null : DECISION[item.decision],
    })),
    page: items.page,
    totalPages: items.totalPages,
    totalCount: items.totalCount,
    counts: { ...counts },
  };
}
