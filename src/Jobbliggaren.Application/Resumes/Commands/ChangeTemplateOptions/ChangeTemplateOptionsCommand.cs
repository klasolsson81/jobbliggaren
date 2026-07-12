using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Resumes.Commands.ChangeTemplateOptions;

/// <summary>
/// Replaces a CV's visual template options — template, accent colour, font pair and
/// density (Fas 4b PR-8b 8b.2, ADR 0096). The write-half of the
/// <see cref="Domain.Resumes.CvTemplateOptions"/> lifecycle
/// (<see cref="Domain.Resumes.Resume.ChangeTemplateOptions"/>, the only mutation path);
/// the builder UI (8b.3) is the first consumer.
/// </summary>
/// <remarks>
/// <para>Carries ONLY the four visual members. The photo members
/// (<c>PhotoEnabled</c>/<c>PhotoShape</c>) are deliberately absent from the contract: the
/// photo image feature is DPIA-gated to PR-10 (ADR 0093 D5f), so the write-path preserves
/// the persisted photo config rather than exposing a way to enable a photo that cannot yet
/// exist — fail-closed by construction, not by validation (dotnet-architect bind 8b.2 Q1).</para>
/// <para>Nyckelfri: a template/display change reads no decrypted CV content and is not a
/// review input, so — unlike <c>SetResumeLanguageCommand</c> — it needs neither
/// <see cref="Common.Security.IRequiresFieldEncryptionKey"/> nor a finding-status
/// reconcile (bind Q4).</para>
/// <para>NOT <c>IAuditableCommand</c>: a non-PII display preference has no downstream
/// consequence and lies outside the audit trail's purpose; because the domain no-op on
/// unchanged options returns <c>Result.Success()</c> without an event, an audit marker
/// would write a dishonest "changed" row on every idempotent PUT (bind Q2).</para>
/// </remarks>
public sealed record ChangeTemplateOptionsCommand(
    Guid ResumeId,
    string Template,
    string AccentColor,
    string FontPair,
    string Density)
    : ICommand<Result>, IAuthenticatedRequest;
