import { z } from "zod";
import { AUTH_ERROR_CODES } from "@/lib/auth/auth-error-codes";
import { readableInstantSchema } from "./_helpers";

/**
 * `POST /api/v1/auth/account-email-change/complete` → 409 (#1975, ADR 0153): a full match before the delay has run.
 * The earliest instant travels as the problem's `completableFrom` extension; nothing was spent.
 */
export const accountEmailChangeNotYetSchema = z.object({
  title: z.literal(AUTH_ERROR_CODES.AccountEmailChangeNotYet),
  completableFrom: readableInstantSchema,
});
