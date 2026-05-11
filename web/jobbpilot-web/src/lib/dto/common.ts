import { z } from "zod";

/**
 * Backend-svar vid POST som skapar en ny resurs och returnerar dess id.
 * Används för POST `/api/v1/applications`, `/api/v1/resumes`, m.fl.
 */
export const createdResourceSchema = z.object({
  id: z.string(),
});

export type CreatedResource = z.infer<typeof createdResourceSchema>;
