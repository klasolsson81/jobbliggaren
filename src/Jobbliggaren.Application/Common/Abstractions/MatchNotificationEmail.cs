using Jobbliggaren.Domain.JobSeekers;

namespace Jobbliggaren.Application.Common.Abstractions;

/// <summary>
/// Vad en bakgrundsmatchnings-notis gäller (ADR 0080 Vag 4 PR-4): <c>Direct</c> =
/// en Topp-matchning skickad direkt i scan-runden; <c>Digest</c> = en kadens-cap:ad
/// sammanfattning av Stark-matchningar.
/// </summary>
public enum MatchNotificationKind
{
    Direct,
    Digest,
}

/// <summary>
/// EN matchning i ett notis-mejl. ENBART icke-PII, namngivna fält: jobbtitel +
/// företagsnamn (publika annons-fält) + grad-LABEL (svensk namngiven kategori,
/// t.ex. "Toppmatch"/"Stark match" — ALDRIG en siffra/procent; Goodhart, ADR
/// 0071/0080). Inget CV-innehåll, inga concept-ids, ingen mottagar-adress.
/// </summary>
public sealed record MatchNotificationItem(
    string JobTitle,
    string CompanyName,
    string GradeLabel);

/// <summary>
/// Innehållskontraktet för bakgrundsmatchnings-notis-mejl (ADR 0080 Vag 4 PR-4).
/// <para>
/// <b>Icke-PII per konstruktion:</b> <see cref="IEmailSender.SendMatchNotificationEmailAsync"/>
/// tar mottagar-adressen som ett SEPARAT argument — denna typ bär den ALDRIG (eller
/// annan PII). <see cref="Items"/> är publika annons-fält + grad-labels; avregistrerings-/
/// inställningslänken byggs template-side ur <c>EmailOptions.BaseUrl</c>.
/// </para>
/// <para>
/// <see cref="TotalCount"/> är det ärliga totalantalet i fönstret (kan vara större än
/// <see cref="Items"/> när en digest cap:as) så mallen kan säga "och N till".
/// <see cref="Cadence"/> är meningsfull endast för <see cref="MatchNotificationKind.Digest"/>.
/// </para>
/// </summary>
public sealed record MatchNotificationEmail(
    MatchNotificationKind Kind,
    DigestCadence? Cadence,
    IReadOnlyList<MatchNotificationItem> Items,
    int TotalCount);
