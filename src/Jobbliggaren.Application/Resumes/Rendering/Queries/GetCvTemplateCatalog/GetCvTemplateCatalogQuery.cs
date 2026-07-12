using Mediator;

namespace Jobbliggaren.Application.Resumes.Rendering.Queries.GetCvTemplateCatalog;

// Fas 4b PR-8b 8b.3 (CTO-bind Q2). Returns the closed, non-PII catalog of CV template options the
// mallbyggare's pickers consume. Deliberately carries NO markers: no IAuthenticatedRequest, no
// IRequiresFieldEncryptionKey — it reads no user context, no owner data, no DEK. Static reference
// data, identical for every user (HTTP-layer .RequireAuthorization() is the only gate). The
// LoggingBehavior still measures it (§2.5); auto-registered by Mediator.SourceGenerator.
public sealed record GetCvTemplateCatalogQuery() : IQuery<CvTemplateCatalogDto>;
