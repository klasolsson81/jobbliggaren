import { z } from "zod";

/**
 * Backend-svar från `POST /api/v1/auth/login` och `POST /api/v1/auth/register`
 * vid lyckad autentisering. Opaque session-id transporteras via cookie efter
 * detta — raw value loggas aldrig (se ADR 0017 § Log and Audit Policy).
 */
export const sessionResponseSchema = z.object({
  sessionId: z.string(),
});

export type SessionResponse = z.infer<typeof sessionResponseSchema>;

/**
 * Backend-svar från `POST /api/v1/auth/register` vid 400 Bad Request. Två former delar
 * statuskoden: `errors` är dictionary per fält → felmeddelanden (FluentValidation via
 * ValidationException-middlewaren), medan `title` bär maskin-felkoden (t.ex.
 * "Auth.PwnedPassword") när felet kommer från Result/DomainError-kanalen som ProblemDetails
 * (#616). Båda fälten är optionella — samma parse täcker båda formerna.
 */
export const registrationValidationErrorSchema = z.object({
  errors: z.record(z.string(), z.array(z.string())).optional(),
  title: z.string().optional(),
});

/**
 * The machine code carried by a ProblemDetails `title`, for responses where the STATUS alone does not
 * identify the cause. `POST /api/v1/auth/register` has two independent 503 producers: the
 * registration kill-switch (`Auth.RegistrationsClosed`, ADR 0083 Amendment 2026-08-03) and the
 * central `SessionStoreUnavailableException` arm, which fires on a Redis outage while registration is
 * OPEN. Discriminating on 503 alone would render "registration is not open yet" for an incident and
 * mask it. Same exact-whitelist discipline the 400 path already applies to "Auth.PwnedPassword".
 */
export const problemTitleSchema = z.object({
  title: z.string().optional(),
});

export type RegistrationValidationError = z.infer<
  typeof registrationValidationErrorSchema
>;
