import { z } from "zod";

// AF activity-report read model (issue #316). Job metadata is nullable: a
// soft-deleted JobAd or a degenerate cover-letter-only application yields no
// employer/title/location; a manual posting has no location. The FE renders a
// neutral "—" and no copy button for an empty field. nullable + optional gives
// deploy-skew resilience (an older cached response missing a field still parses).
export const activityReportItemDtoSchema = z.object({
  applicationId: z.string(),
  appliedAt: z.string(),
  employer: z.string().nullable().optional(),
  title: z.string().nullable().optional(),
  location: z.string().nullable().optional(),
  source: z.string().nullable().optional(),
  url: z.string().nullable().optional(),
});
export type ActivityReportItemDto = z.infer<typeof activityReportItemDtoSchema>;

export const activityReportDtoSchema = z.object({
  year: z.number().int(),
  month: z.number().int(),
  applications: z.array(activityReportItemDtoSchema),
});
export type ActivityReportDto = z.infer<typeof activityReportDtoSchema>;
