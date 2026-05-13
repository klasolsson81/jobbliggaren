using System.Text.Json.Serialization;

namespace JobbPilot.Application.JobAds.Commands.RedactRecruiterPii;

/// <summary>
/// Identifier-typ för rekryterar-PII-radering. Email är primär identifier i
/// JobTech-payloads (employer.contact_email). Name är defererad till TD-75 —
/// kräver multi-path jsonb-sökning + ev. full-text på description.text och
/// tas in när första faktiska name-baserade request kommer. Per ADR 0032 §8
/// amendment 2026-05-13.
///
/// <para>
/// JsonStringEnumConverter aktiverat lokalt så admin-endpointen kan ta emot
/// <c>"Email"</c>/"Name" i request-body — Api:n har inte global string-enum-
/// config. Lokal annotation undviker global JSON-opts-skifte i Fas 2.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<RecruiterIdentifierType>))]
public enum RecruiterIdentifierType
{
    Email,
    Name
}
