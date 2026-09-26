/**
 * Every `Auth.*` machine code this client compares a ProblemDetails `title` against (#1293).
 *
 * The backend emits these strings and the client discriminates on them, so a rename on either side
 * alone kills a client arm with nothing failing: both suites pin their own literal. The join is
 * `tests/Jobbliggaren.Architecture.Tests/AuthErrorCodeWireContractTests.cs`, which reads this object
 * as source text. Each key is the name of the C# constant on `AuthErrorCodes` that carries the value.
 *
 * Compared, never rendered: backend text stays out of the UI (`lib/http/problem.ts`).
 */
export const AUTH_ERROR_CODES = {
  RegistrationsClosed: "Auth.RegistrationsClosed",
  EmailDeliveryUnavailable: "Auth.EmailDeliveryUnavailable",
  LoginCodeWrongLastAttempt: "Auth.LoginCodeWrongLastAttempt",
  LoginCodeBurned: "Auth.LoginCodeBurned",
  LoginCodeWrong: "Auth.LoginCodeWrong",
  ReauthCooldown: "Auth.ReauthCooldown",
  ReauthCodeBudgetExhausted: "Auth.ReauthCodeBudgetExhausted",
  EmailTaken: "Auth.EmailTaken",
  ChangeEmailTargetBudgetExhausted: "Auth.ChangeEmailTargetBudgetExhausted",
  EmailChangeIncomplete: "Auth.EmailChangeIncomplete",
  ExternalEmailUnverified: "Auth.ExternalEmailUnverified",
} as const;
