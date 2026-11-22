import { z } from "zod";
import { isValidId } from "@/lib/validation/guid";
import { pagedResultWithTotalPages, readableInstantSchema } from "./_helpers";

/**
 * One row of the open junior IT list (`GET /api/v1/public/job-ads/junior`, ADR 0186): the catalogue row's six members
 * and the ad's last publication day, and no other. Strict, like the catalogue's: a member the backend adds must stop
 * the page rather than slip into it unread. `PublicJuniorItJobAdsBoundaryTests` holds these keys equal to
 * `PublicJuniorItJobAdDto`.
 */
export const publicJuniorItJobAdSchema = z
  .object({
    id: z.string().refine(isValidId),
    title: z.string(),
    companyName: z.string(),
    municipalityName: z.string().nullable(),
    regionName: z.string().nullable(),
    publishedAt: readableInstantSchema,
    expiresAt: readableInstantSchema.nullable(),
  })
  .strict();
export type PublicJuniorItJobAd = z.infer<typeof publicJuniorItJobAdSchema>;

/** A kommun the list can be narrowed to, with its number of listed junior IT ads. */
export const publicJuniorItPlaceSchema = z
  .object({
    municipalityConceptId: z.string(),
    name: z.string(),
    count: z.number().int().positive(),
  })
  .strict();
export type PublicJuniorItPlace = z.infer<typeof publicJuniorItPlaceSchema>;

/**
 * One page of the list, every kommun in the whole list, and whether the whole list meets the floor for indexing (ADR
 * 0166, the Amendment 2026-11-22). The floor's outcome is required: an answer without it is unreadable, so the page is
 * an outage and never `index`.
 */
export const publicJuniorItJobAdsPageSchema = z
  .object({
    rows: pagedResultWithTotalPages(publicJuniorItJobAdSchema).strict(),
    places: z.array(publicJuniorItPlaceSchema),
    floorHolds: z.boolean(),
  })
  .strict();
export type PublicJuniorItJobAdsPage = z.infer<typeof publicJuniorItJobAdsPageSchema>;
