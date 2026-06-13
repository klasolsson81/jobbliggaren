using System.Text.Json.Serialization;

namespace Jobbliggaren.Migrate;

// AWS-managerad RDS-master-secret-shape. PascalCase per .NET-konvention,
// mappas till snake_case-JSON via JsonPropertyName.
internal sealed record RdsMasterSecret(
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("password")] string Password);
