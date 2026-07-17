import { z } from "zod";

// AF activity-report read model (issue #316). Job metadata is nullable: a
// cover-letter-only application (no ad row at all) yields no employer/title/
// location; a manual posting has no location. #805-3 truth-sync: the previous
// claim ("a soft-deleted JobAd") was false — JobAd.DeletedAt has no writer (#821),
// so a retracted ad is ARCHIVED and still joins, metadata intact. The FE renders a
// neutral "Saknas" placeholder and no copy button for an empty field. nullable +
// optional gives deploy-skew resilience (an older cached response missing a
// field still parses).
export const activityReportItemDtoSchema = z.object({
  applicationId: z.string(),
  appliedAt: z.string(),
  employer: z.string().nullable().optional(),
  title: z.string().nullable().optional(),
  location: z.string().nullable().optional(),
  source: z.string().nullable().optional(),
  url: z.string().nullable().optional(),
  // #892 (CTO R1): källannonsens livscykel-status ("Active" | "Archived" |
  // "Erased") — den strukturella signalen borttagen-markören keyar på. En
  // raderad annons rad visar den bevarade snapshot-identiteten (eller "Saknas"
  // utan snapshot) och får inte se levande ut. null ⟺ manuell ansökan (ingen
  // livs-utsaga — #805-3-idiomet). Lös z.string(): okänt framtida värde
  // degraderar, hard-failar aldrig hela rapporten.
  adStatus: z.string().nullable().optional(),
});
export type ActivityReportItemDto = z.infer<typeof activityReportItemDtoSchema>;

export const activityReportDtoSchema = z.object({
  year: z.number().int(),
  month: z.number().int(),
  applications: z.array(activityReportItemDtoSchema),
});
export type ActivityReportDto = z.infer<typeof activityReportDtoSchema>;
