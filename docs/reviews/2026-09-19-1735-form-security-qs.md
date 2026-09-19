# #1735 (epic #1732 part 1a) — pre-code scoped questions Q-S1..Q-S4 (security-auditor, 2026-09-19)

> Transcribed verbatim by the driving session. Questions routed by senior-cto-advisor (`2026-09-19-1735-form-cto.md` §8), asked report-only before the store, index and grant code. No escalation to Klas.

## Security audit: #1735 pre-code scoped questions Q-S1 to Q-S4 (report-only)

**Status:** R1 is confirmed. One binding changes before the store is written: CTO point 12, where R8 keys the address index on "code-bearing". Built that way, it is a **Major**.
**Authority:** GDPR Art. 5(1)(a), 5(2), 32(1)(b) · ADR 0142 D1 (:77, :96, :105-107), D2 (:148) and D3 (:169-172) · CLAUDE.md §9.6 · charter areas 3 and 6.
**Measured against:** `C:/tmp/jbl-1735` at `ed0065b7`. HEAD moved from `63fab3b4` while I was measuring, and `ed0065b7` touches none of the files cited here. The ASP.NET Core Identity source is read at tag `v10.0.10`, which `Directory.Packages.props:86` pins, fetched 2026-09-19. I edited nothing, built nothing, ran no tests and filed nothing.

**Q-S1 (R1): burning only the code arm is confirmed, but the third removal cause is a Major if built as worded.**
- **What holds.**
  - The guess limit is untouched. A burned record answers Burned without comparing anything, and the link never had an attempt budget (ADR :105-107).
  - "Exactly three ways out of Redis" is complete on the declared deploy config: `noeviction` plus AOF (`deploy/docker-compose.yml:654-661`).
- **What breaks.** Take "a newer **code-bearing** Put" (R8, architect `:142`, CTO `:293`) together with 1a's plan, where only `Active`+`Admitted` mints a code (CTO point 7). Supersession then depends on whether the account exists.
  - The probe: mint twice for X, then verify the first id. An active account answers 410 `LoginCodeExpired`; every other address answers 400 `LoginCodeWrong`.
  - It is unconditional and costs two mails.
  - A Redis reader, who is in scope per ADR :96, gets the same answer from whether `challenge-by-address/v1/{hex}` exists.
  - This breaks D3's "carries no account-existence information" (:169-172). It is the same oracle I graded Major in Q2(b).
- **Required:** key the index swap on the request path's `CodeBudget == Admitted`, which does not depend on the account, and not on whether the record carries a code.
  - A Put whose `CodeBudget` is `Exhausted` never swaps the index, so (A)'s "a link-only record never burns the live code challenge" still holds.
  - Klas's (A) promise is unaffected.

**Q-S2 (R8): Minor, but the count is wrong. The bound is at most 8 live records per address in total, not about 6 plus 1.**
- **The derivation.** The live records are the admitted mints in the last 900 s. A 900 s span can hold three `MailBudget` windows' mints: the last two of one fixed 600 s window, all three of the next, and three from the one after.
  - Example: mints at T−890, T−830 | T−720, T−660, T−600 | T−120, T−60, T.
  - The windows open at T−1320, T−720 and T−120, and every mint is at least 60 s after the previous one, so this is reachable at the default cooldown.
- Under the Q-S1 keying, at most one of the 8 comes from an admitted mint, and that is the only kind that can carry a code.
- **Why only Minor:**
  - No effect on the guess limit: links carry no code, and codes are bounded by `CodeBudget`.
  - No oracle: the same count applies to code-less records for any address.
  - Every link lands in the owner's own inbox, where whoever reads it could mint a link anyway.
- **What grows** is the exposure of single-use, 15-minute bearer tokens to mail scanners, forwarding and shared screens. It stays below the shipped reset path's 60 per hour.
- **Disposition:** record it as a named residual in the 1a amendment. Revoking the sibling links on a successful consume is not required.

**Q-S3 (R14/F5): yes, this is the single Identity write I required. The audit record must be an `audit_log` row; built as the ops line alone, that is a Major.**
- **The write, measured in v10.0.10:**
  - `RemovePasswordAsync` (`UserManager.cs:921-938`) sets the hash to null and rotates the stamp in memory only (`UserManager.cs:2807-2822` → `UserStoreBase.cs:273-280, 726-734`; neither setter saves).
  - It then makes one `UpdateUserAsync` (`:2963-2975`), which calls `UserStore.UpdateAsync` (`UserStore.cs:212-229`): `Context.Update` plus one `SaveChanges`.
  - So a flag set on the same `ApplicationUser` instance goes out in the same UPDATE.
- **Condition (a):** no separate `UpdateAsync` writes the flag first. The ADR 0127 precedent does exactly that (`UserAccountService.cs:433-434`), and it is safe there only because the hash had already been replaced.
- **Condition (b):** a result that is not `Succeeded` throws, so no invalidation, session or audit row follows a write that did not happen.
  - `ConcurrencyFailure` is reachable (`UserStore.cs:225`): two first proofs can race on two live records.
  - Once the write has committed, invalidate and create run on `CancellationToken.None` (`AuthEndpoints.cs:129-136`).
- **The audit record:**
  - The house rule is at `IAuthAuditLogger.cs:31-38`: "the completed reset IS auditable" — a completed credential change on a known user id goes to `audit_log`.
  - This write is both `User.EmailConfirmed` (`VerifyEmailCommand.cs:24-27`) and a credential change like `User.PasswordReset` (`ResetPasswordCommand.cs:28-31`). It happens after proof, so the row reveals nothing about whether an account exists.
  - F5's premise holds for `AuditBehavior` only. Adding a row on one branch alone is already done twice: `AutoPromoteParsedResumeCommandHandler.cs:159-167` and `ImportResumeCommandHandler.cs:267-275`, both persisted by `UnitOfWorkBehavior.cs:16`.
  - **Required:** add one row, only on `FirstProofRecorded`, with aggregate `User` (for example `User.InboxProvenByLogin`). Event 1013 may stay as an ops signal, but it does not replace the row.

**Q-S4 (my Q15 condition 1): yes, it applies to production code only. There is one test binding; building it otherwise is a Minor.**
- **Why the test caller is fine:**
  - `Jobbliggaren.Infrastructure` grants `InternalsVisibleTo` only to test and QA assemblies (`csproj:218-230`), so no `src` host can call an internal extension.
  - `ApiFactory` always runs as Development (`ApiFactory.cs:85`).
  - None of the five Production hosts derives from `ApiFactory`.
- **The binding:**
  - `ApiFactory` re-adds the capture, and the Production hosts will call `RemoveAll<IEmailSender>` (F8). So neither integration host can see whether production code registered it.
  - Condition (1) must therefore be proven in two places:
    - a descriptor pin over the unswapped `AddEmailSender` + `AddDevOnlyTestingSupport` composition, in both environments;
    - a Production-host check that `DevLoginCodeCapture` and its `Application/Dev` read port are absent. That precedent is `ProductionStartupSmokeTests.cs:266-271`, and neither type is removed by the `IEmailSender` swap.
  - If the only proof runs through a swapping host, the pin cannot fail. That is a Minor: the map gate still holds, and only reserved recipients are captured.

### Escalation to Klas
None. R1 is confirmed, so the STOPP condition in CTO §8 does not fire. The Q-S1 Major is a binding on code not yet written, and it closes with a code-level change that leaves Klas's (A) promise intact.

Files cited:
- C:/tmp/jbl-1735/docs/decisions/0142-passwordless-auth-one-page-code-or-link-oauth-ready.md
- C:/DOTNET-UTB/JobbPilot/docs/reviews/2026-09-19-1735-form-cto.md
- C:/DOTNET-UTB/JobbPilot/docs/reviews/2026-09-19-1735-form-architect.md
- C:/tmp/jbl-1735/deploy/docker-compose.yml
- C:/tmp/jbl-1735/src/Jobbliggaren.Infrastructure/Auth/UserAccountService.cs
- C:/tmp/jbl-1735/src/Jobbliggaren.Api/Endpoints/AuthEndpoints.cs
- C:/tmp/jbl-1735/src/Jobbliggaren.Application/Common/Abstractions/IAuthAuditLogger.cs
- C:/tmp/jbl-1735/src/Jobbliggaren.Application/Auth/Commands/VerifyEmail/VerifyEmailCommand.cs
- C:/tmp/jbl-1735/src/Jobbliggaren.Application/Auth/Commands/ResetPassword/ResetPasswordCommand.cs
- C:/tmp/jbl-1735/src/Jobbliggaren.Application/Resumes/Commands/AutoPromoteParsedResume/AutoPromoteParsedResumeCommandHandler.cs
- C:/tmp/jbl-1735/src/Jobbliggaren.Application/Resumes/Commands/ImportResume/ImportResumeCommandHandler.cs
- C:/tmp/jbl-1735/src/Jobbliggaren.Application/Common/Behaviors/UnitOfWorkBehavior.cs
- C:/tmp/jbl-1735/src/Jobbliggaren.Infrastructure/Jobbliggaren.Infrastructure.csproj
- C:/tmp/jbl-1735/tests/Jobbliggaren.Api.IntegrationTests/Infrastructure/ApiFactory.cs
- C:/tmp/jbl-1735/tests/Jobbliggaren.Api.IntegrationTests/Configuration/ProductionStartupSmokeTests.cs
- Identity v10.0.10 source (`UserManager.cs`, `UserStoreBase.cs`), saved in the scratchpad: C:/Users/zebac/AppData/Local/Temp/claude/c--DOTNET-UTB-JobbPilot/27bdf0eb-ec1c-4fcf-aa26-51e64ed1123a/scratchpad/. `UserStore.cs` was fetched from the same tag and not saved.