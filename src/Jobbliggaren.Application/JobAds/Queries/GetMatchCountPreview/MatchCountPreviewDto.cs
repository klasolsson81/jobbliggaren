namespace Jobbliggaren.Application.JobAds.Queries.GetMatchCountPreview;

/// <summary>
/// Epik #526 (ADR 0089) — svaret för live sök-preview-räknaren: antalet aktiva annonser som
/// matchar utkastets sök-facetter. Ett wrappat <c>{ count: int }</c>-objekt (inte en bar int) så
/// wire-formen är ett evolverbart JSON-objekt.
/// <para>
/// <b>Egen typ, aldrig återbrukad <c>MyMatchCountDto</c> (CCP/SRP):</b> de två räknarna mäter
/// genuint olika frågor (sök-preview vs sparad grad-match) och ändras av olika skäl. Att den
/// binära wire-formen råkar vara identisk är strukturlikhet, inte delad kunskap. Ingen
/// <c>Zero</c>-sentinel: sök-preview har inget "inget angivet"-tillstånd — tomt utkast ger den
/// ärliga totalsumman (alla aktiva annonser), inte 0. <c>Count</c> är alltid ett konkret värde
/// (record class, aldrig null → tom-body-fällan inträffar aldrig); FE äger <c>null</c> som ren
/// klient-loading-state.
/// </para>
/// </summary>
public sealed record MatchCountPreviewDto(int Count);
