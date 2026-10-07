using System.Text.Json.Serialization;
using Jobbliggaren.Application.Auth.Access;

namespace Jobbliggaren.Infrastructure.Auth.Access;

internal sealed record SecurityProofPayload(
    [property: JsonPropertyName("f")] long? FlowEpoch,
    [property: JsonPropertyName("u")] Guid? UserId,
    [property: JsonPropertyName("r")] long? AccessRevision)
{
    public static SecurityProofPayload Maximum { get; } = new(long.MaxValue, Guid.Empty, long.MaxValue);
    public static SecurityProofPayload From(AccountAccessProof proof) => new(proof.FlowEpoch, proof.UserId, proof.AccessRevision);
    public AccountAccessProof? Decode() =>
        FlowEpoch is >= 0 && UserId != Guid.Empty && AccessRevision is null or >= 0
        && ((UserId is null) == (AccessRevision is null))
            ? new AccountAccessProof(FlowEpoch.Value, UserId, AccessRevision) : null;
}
