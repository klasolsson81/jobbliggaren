namespace Jobbliggaren.Application.Common.Security;

/// <summary>
/// TD-13 (ADR 0049 Mekanik-not 3/4) — markör på commands/queries vars
/// aggregat bär krypterade kolumner (cover_letter / application_notes.content
/// / follow_ups.note / resume_versions.content_enc). Opt-in för
/// <c>FieldEncryptionKeyPrefetchBehavior</c>: behaviorn värmer ägar-DEK i den
/// scope-bundna cachen INNAN handlern materialiserar entiteten, så
/// <c>FieldDecryptionMaterializationInterceptor</c> blir en ren synkron
/// cache-hit (EF Core 10:s InitializedInstance är synkron; §3.5 förbjuder
/// sync-over-async). Meddelanden utan markören gör ingen DEK-op.
/// </summary>
public interface IRequiresFieldEncryptionKey;
