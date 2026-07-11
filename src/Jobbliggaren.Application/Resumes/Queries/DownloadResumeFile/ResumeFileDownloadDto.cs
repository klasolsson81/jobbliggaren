namespace Jobbliggaren.Application.Resumes.Queries.DownloadResumeFile;

/// <summary>
/// Fas 4b PR-9b (ADR 0100 §D3 read-path). The transport shape for a downloaded original CV file:
/// the DECRYPTED plaintext bytes, the server-derived canonical content-type, and the
/// (personnummer-redacted) filename. Plaintext bytes flow ONLY through this Application transport
/// DTO (exactly like the render path's byte[] PDF result) and out the HTTP response body — the
/// <c>ResumeFile</c> aggregate never grows a plaintext-bytes member (aggregate-honesty pin, ADR
/// 0100 CTO Q2). Never logged, never cached (M-F2).
/// </summary>
public sealed record ResumeFileDownloadDto(byte[] Content, string ContentType, string FileName);
