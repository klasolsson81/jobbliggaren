using System.Security.Cryptography;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Infrastructure.Security;

/// <summary>
/// #198 gate M-3 — re-wraps every stored per-user DEK from a retiring master key to a new one.
///
/// <para>
/// <b>What a master-key rotation is, and what it is not.</b> Each row in
/// <c>user_data_keys</c> holds a DEK wrapped under the master key. Rotating the master key means
/// unwrapping each DEK with the old key and wrapping the same DEK bytes under the new one. The
/// DEK never changes, so <b>no field ciphertext is touched</b> and <c>dek_version</c> is
/// preserved — <c>UserDataKeyStore</c>'s own comment draws exactly this line, carving this
/// operation out of #501's DEK-rotation axis (which bumps the version and would require a
/// re-encrypt migration).
/// </para>
///
/// <para>
/// <b>Idempotency marker: <c>cmk_key_id</c>.</b> Rows are selected by the retiring identity and
/// stamped with the new one, so a completed run leaves nothing to select. A second run finds
/// zero rows and succeeds — and that is M-3's idempotency proof rather than a convenience.
/// </para>
///
/// <para>
/// <b>Compare-and-swap, not a tracked update.</b> The write predicate re-states
/// <c>CmkKeyId == oldKeyId</c>, so a row that changed underneath the scan updates zero rows and
/// the run fails loudly instead of overwriting someone else's work.
/// <b>Declared unreachable single-threaded, and therefore deliberately unpinned</b>
/// (CLAUDE.md §5): the CAS terms, the <c>affected != 1</c> guard and the in-loop round-trip
/// cannot fire while this runs as the documented stopped-world operation — <c>pending</c> is
/// filtered on exactly that identity, <c>(JobSeekerId, DekVersion)</c> is the primary key, and
/// the round-trip goes through the same <c>Wrap</c> it verifies. Mutation-verified 2026-08-09:
/// removing any of the three leaves the suite green, while removing the transaction, the
/// byte-difference guard, the foreign-identity guard or the post-commit pass each turns it red.
/// (Method, because the first attempt at this measurement was worthless: the mutated code failed
/// to compile and the suite ran against a stale assembly. Mutate, rebuild, ASSERT THE OUTPUT
/// ASSEMBLY TIMESTAMP MOVED, then run.) The same applies to the 32-byte length guard on
/// <c>WrapDataKey</c> and to this pass's identity branch.
/// What would make them reachable, so the next person knows when to pin them: a concurrent
/// writer — which the stopped-world procedure forbids — for the CAS terms and
/// <c>affected != 1</c>; a second <c>dek_version</c> for the version term (#501); and, for the
/// in-loop round-trip, nothing at all — it is a regression guard on <c>Wrap</c>/<c>BuildAad</c>
/// and the wire layout, structurally unpinnable from outside because making it fire requires
/// <c>Wrap</c> and <c>Unwrap</c> to disagree.
/// <c>affected != 1</c> is NOT a duplicate of the CAS predicate: it fails INSIDE the
/// transaction, where the post-commit pass would catch the same skipped row only after commit. <c>UserDataKey</c> has no
/// mutator by design; adding one whose only caller is this operation would widen the entity for
/// everyone and route the write through the change tracker, losing the atomic guard that makes
/// "affected != 1" meaningful.
/// </para>
///
/// <para>
/// <b>Runs offline.</b> The caller is the Migrate host, which builds its <c>AppDbContext</c>
/// without the field-encryption interceptors — so this performs no audit side-effects and
/// materialises no DEK-bearing aggregate in a system job. api and worker must be stopped: the
/// operator procedure in <c>docs/runbooks/master-key-ops.md</c> §4 stops them and the reconcile
/// timer first, because a concurrent first-use would insert a row under the old identity behind
/// the scan.
/// </para>
/// </summary>
public sealed class MasterKeyRewrapper(
    LocalDataKeyProvider retiringKey,
    LocalDataKeyProvider incomingKey,
    string retiringKeyId,
    string incomingKeyId)
{
    /// <summary>Outcome of one run. <paramref name="Rewrapped"/> is 0 on a repeat run.</summary>
    public sealed record Result(int Rewrapped, int AlreadyCurrent, int Verified)
    {
        /// <summary>Every row the scan saw, whatever identity it carried.</summary>
        public int Scanned => Rewrapped + AlreadyCurrent;
    }

    public async Task<Result> RewrapAllAsync(AppDbContext db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);

        if (string.Equals(retiringKeyId, incomingKeyId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Retiring and incoming key identity are both '{retiringKeyId}'. A rotation must " +
                "change the identity, or the marker cannot distinguish rotated rows from " +
                "un-rotated ones.");
        }

        // AND THE BYTES MUST DIFFER, not only the labels. The identity guard above checks the
        // stickers; this checks the keys. Without it, pointing both _FILE pointers at the same
        // file succeeds end to end — unwrap, wrap, round-trip, CAS and post-commit verification
        // all pass — and the tool reports "Re-wrap COMPLETE" while the retiring key still opens
        // every row. That is not a hypothetical path: it is the natural repair when an operator
        // finds the retiring key missing mid-rotation and copies the live file over it.
        //
        // AES-GCM auth-fails under any other key, so if the INCOMING key can open something the
        // RETIRING key wrapped, the two are the same key. The probe is a throwaway value, not a
        // stored row: that works on an empty table and touches no production data.
        await EnsureMasterKeysDifferAsync(retiringKey, incomingKey, ct).ConfigureAwait(false);

        // ORDERED, and that is production behaviour rather than a test affordance: a rotation
        // that visits rows in an undefined order makes its own logs unreadable, makes resuming
        // ambiguous, and makes any test of "row N failed after row 1 was written" depend on
        // which row the heap happened to return first.
        var rows = await db.Set<UserDataKey>()
            .AsNoTracking()
            .OrderBy(k => k.JobSeekerId)
            .ThenBy(k => k.DekVersion)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // A row stamped with neither identity means the tool is pointed at a database rotated by
        // something else, or at the wrong retiring key. Refuse before writing anything: a partial
        // rotation is the one state with no clean recovery.
        var foreign = rows
            .Where(r => r.CmkKeyId != retiringKeyId && r.CmkKeyId != incomingKeyId)
            .ToList();
        if (foreign.Count > 0)
        {
            throw new InvalidOperationException(
                $"{foreign.Count} row(s) carry an unexpected cmk_key_id (first: owner " +
                $"{foreign[0].JobSeekerId.Value:D}, id '{foreign[0].CmkKeyId}'). Expected " +
                $"'{retiringKeyId}' or '{incomingKeyId}'. Nothing was written.");
        }

        var pending = rows.Where(r => r.CmkKeyId == retiringKeyId).ToList();
        var alreadyCurrent = rows.Count - pending.Count;

        // ONE TRANSACTION over every row. At beta scale user_data_keys is a handful of rows, so
        // the cost is nil and the property is worth everything: a crash mid-run rolls back to an
        // untouched database, and the re-run starts from zero rather than from a mixed state
        // nobody can characterise. If scale ever forces chunking, the per-row CAS below already
        // supports resuming on the marker — only the transaction scope would change.
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        foreach (var row in pending)
        {
            var owner = row.JobSeekerId;
            byte[]? dek = null;
            try
            {
                dek = await retiringKey.UnwrapDataKeyAsync(owner, row.WrappedDek, ct)
                    .ConfigureAwait(false);

                var rewrapped = incomingKey.WrapDataKey(dek, owner);

                // Round-trip before writing: prove the new blob yields the SAME DEK bytes. A bug
                // that generated a fresh DEK instead of re-wrapping the existing one passes every
                // "it unwraps" check and destroys all field data for that owner.
                var check = await incomingKey.UnwrapDataKeyAsync(owner, rewrapped, ct)
                    .ConfigureAwait(false);
                try
                {
                    if (!CryptographicOperations.FixedTimeEquals(dek, check))
                    {
                        throw new CryptographicException(
                            $"Re-wrap round-trip produced different DEK bytes for owner " +
                            $"{owner.Value:D}. Nothing is committed.");
                    }
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(check);
                }

                var affected = await db.Set<UserDataKey>()
                    .Where(k => k.JobSeekerId == owner
                                && k.DekVersion == row.DekVersion
                                && k.CmkKeyId == retiringKeyId)
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(k => k.WrappedDek, rewrapped)
                              .SetProperty(k => k.CmkKeyId, incomingKeyId),
                        ct)
                    .ConfigureAwait(false);

                if (affected != 1)
                {
                    throw new InvalidOperationException(
                        $"Compare-and-swap updated {affected} rows for owner {owner.Value:D} " +
                        $"(dek_version {row.DekVersion}); expected exactly 1. The row changed " +
                        "underneath the scan. Nothing is committed.");
                }
            }
            finally
            {
                if (dek is not null)
                {
                    CryptographicOperations.ZeroMemory(dek);
                }
            }
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);

        var verified = await VerifyAllUnwrapUnderIncomingKeyAsync(db, ct).ConfigureAwait(false);
        return new Result(pending.Count, alreadyCurrent, verified);
    }

    /// <summary>
    /// Proves the two providers hold different key material. Wraps a throwaway value under the
    /// retiring key and asserts the incoming key CANNOT open it — AES-GCM auth-fails under any
    /// other key, so a successful unwrap means the keys are identical.
    /// </summary>
    private static async Task EnsureMasterKeysDifferAsync(
        LocalDataKeyProvider retiring, LocalDataKeyProvider incoming, CancellationToken ct)
    {
        // A throwaway owner and a throwaway DEK: this never touches stored data, and the probe
        // value is discarded either way.
        var probeOwner = new JobSeekerId(Guid.NewGuid());
        var probe = RandomNumberGenerator.GetBytes(32);
        try
        {
            var wrapped = retiring.WrapDataKey(probe, probeOwner);
            byte[]? opened = null;
            try
            {
                opened = await incoming.UnwrapDataKeyAsync(probeOwner, wrapped, ct)
                    .ConfigureAwait(false);
            }
            catch (CryptographicException)
            {
                return; // the incoming key cannot open it — the keys differ, which is required
            }
            finally
            {
                if (opened is not null)
                {
                    CryptographicOperations.ZeroMemory(opened);
                }
            }

            throw new InvalidOperationException(
                "The retiring and incoming master keys are the SAME key material (different " +
                "identities). Rotating would stamp rows as rotated while leaving them wrapped " +
                "under the key being retired — which defeats the rotation entirely. Nothing was " +
                "written. Check that the two *_FILE pointers reference different files.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(probe);
        }
    }

    /// <summary>
    /// Post-commit proof: every row now carries the incoming identity AND unwraps under the
    /// incoming key. Read fresh from the database rather than from the in-memory list, so this
    /// measures what was persisted.
    /// </summary>
    private async Task<int> VerifyAllUnwrapUnderIncomingKeyAsync(
        AppDbContext db, CancellationToken ct)
    {
        var rows = await db.Set<UserDataKey>()
            .AsNoTracking()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var row in rows)
        {
            if (row.CmkKeyId != incomingKeyId)
            {
                throw new InvalidOperationException(
                    "POST-COMMIT: the re-wrap IS committed and the incoming key MUST be kept. " +
                    $"Owner {row.JobSeekerId.Value:D} still carries cmk_key_id '{row.CmkKeyId}', " +
                    $"expected '{incomingKeyId}'. Re-run to finish; do NOT restore the old key.");
            }

            var dek = await incomingKey.UnwrapDataKeyAsync(row.JobSeekerId, row.WrappedDek, ct)
                .ConfigureAwait(false);
            CryptographicOperations.ZeroMemory(dek);
        }

        return rows.Count;
    }
}
