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
 * Backend-svar från `POST /api/v1/auth/register` vid 400 Bad Request.
 * `errors` är dictionary per fält → felmeddelanden (ASP.NET Core
 * `ValidationProblemDetails`-shape).
 */
export const registrationValidationErrorSchema = z.object({
  errors: z.record(z.string(), z.array(z.string())).optional(),
});

export type RegistrationValidationError = z.infer<
  typeof registrationValidationErrorSchema
>;
