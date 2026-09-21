/**
 * Every `Auth.*` machine code this client compares a ProblemDetails `title` against (#1293).
 *
 * The backend emits these strings and the client discriminates on them, so a rename on either side
 * alone kills a client arm with nothing failing: both suites pin their own literal. The join is
 * `tests/Jobbliggaren.Architecture.Tests/AuthErrorCodeWireContractTests.cs`, which reads this object
 * as source text. Each key is the name of the C# constant on `AuthErrorCodes` that carries the value,
 * with one exception that test names: `PwnedPassword` is composed at runtime from an Identity error
 * code and has no constant of its own.
 *
 * Compared, never rendered: backend text stays out of the UI (`lib/http/problem.ts`).
 */
export const AUTH_ERROR_CODES = {
  PwnedPassword: "Auth.PwnedPassword",
  ChangeEmailCooldown: "Auth.ChangeEmailCooldown",
  RegistrationsClosed: "Auth.RegistrationsClosed",
  EmailDeliveryUnavailable: "Auth.EmailDeliveryUnavailable",
  LoginCodeWrong: "Auth.LoginCodeWrong",
  LoginCodeWrongLastAttempt: "Auth.LoginCodeWrongLastAttempt",
  LoginCodeBurned: "Auth.LoginCodeBurned",
  LoginCodeExpired: "Auth.LoginCodeExpired",
  LoginLinkUnusable: "Auth.LoginLinkUnusable",
  LoginGrantUnusable: "Auth.LoginGrantUnusable",
} as const;
