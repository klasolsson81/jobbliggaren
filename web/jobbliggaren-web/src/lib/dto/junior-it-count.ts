import { z } from "zod";

/**
 * ADR 0188 (#2265) — `GET /api/v1/job-ads/junior-it-count`: the count behind the junior quick link in the /jobb hero,
 * `/junior`'s own number for a signed-in user. Strict, so a member added on the backend fails the parse and the link
 * stays hidden instead of rendering a field nobody reviewed.
 */
export const juniorItCountSchema = z
  .object({
    count: z.number().int().nonnegative(),
  })
  .strict();
export type JuniorItCount = z.infer<typeof juniorItCountSchema>;
