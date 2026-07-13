using System.Data;
using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Jobbliggaren.Infrastructure.CompanyRegister;

/// <summary>
/// #560 kriterie-vågen PR-2 — the <see cref="ICompanyWatchBrowseQuery"/> implementation: paginated
/// array-overlap browse over the local <c>company_register</c> replica. Raw parametrized SQL against
/// the concrete <see cref="AppDbContext"/>, exactly like <see cref="ScbCompanyRegisterStore"/> (the
/// register is Infrastructure-internal — it is NOT a <c>DbSet</c> on <c>IAppDbContext</c>, DPIA C-D4).
///
/// <para>
/// <b>Raw SQL is not a shortcut here — it is the whole point (dotnet-architect Q5).</b> The SNI half
/// of the predicate MUST be emitted as the Postgres array-overlap operator <c>&amp;&amp;</c>, because
/// that is the only shape <c>ix_company_register_sni_codes_gin</c> (GIN, <c>array_ops</c>) can serve.
/// Npgsql does not reliably translate LINQ to <c>&amp;&amp;</c>: the natural
/// <c>.Where(c =&gt; c.SniCodes.Any(s =&gt; userSni.Contains(s)))</c> compiles to an <c>unnest</c>
/// subquery which the GIN index cannot answer — the query still returns the right rows, so every
/// semantic test stays green while the index silently does nothing. PR-1's index would be pure
/// cosmetics. <c>CompanyWatchBrowseQueryPlanTests</c> pins the emitted plan against the GIN index BY
/// NAME, and that pin is mutation-verified against the naive shape.
/// </para>
///
/// <para>
/// <b>Command construction is SPOT'd through <see cref="BuildItemsCommand"/> /
/// <see cref="BuildCountCommand"/>, which the EXPLAIN test calls directly</b> (via the existing
/// <c>InternalsVisibleTo</c>). A test that re-types the SQL by hand is not an oracle — this repo has
/// already shipped exactly that lie: <c>Jobbliggaren.Migrate</c>'s <c>explain-search</c> tool
/// hand-wrote the search SQL, drifted from the production predicate, and (in its own words) "lied in
/// the REASSURING direction". The factories carry the parameter TYPES too, not just the text: binding
/// <c>@sni</c> as <c>text</c> instead of <c>text[]</c> would EXPLAIN a different plan.
/// </para>
/// </summary>
internal sealed class CompanyWatchBrowseQuery(AppDbContext db) : ICompanyWatchBrowseQuery
{
    /// <summary>
    /// The predicate — single-sourced so the count query and the page query can NEVER drift apart (a
    /// drift here is a silently wrong total, not a crash).
    ///
    /// <para>
    /// <b><c>status = @status</c> is deliberately POSITIVE polarity.</b>
    /// <see cref="ScbCompanyRegisterStore.DeregisterMissingAsync"/> uses the negative form
    /// (<c>status &lt;&gt; 'Deregistered'</c>) — correct THERE (it is the sweep's "not already dead"
    /// guard), but importing it here would be a latent vacuous filter: the day
    /// <see cref="CompanyRegisterStatus"/> gains a third member, the negative form starts silently
    /// SURFACING it. DPIA M-D6 says Active, always. Positive polarity makes that true by construction
    /// rather than by vigilance — the #805-3 failure shape.
    /// </para>
    /// </summary>
    private const string FromWhere = """
        FROM company_register
        WHERE status = @status
          AND sate_kommun_code = ANY(@kommun)
          AND sni_codes && @sni
        """;

    // ORDER BY is TOTAL: company_name is not unique in a real register (duplicate legal names are
    // normal), and Postgres sorts are not stable, so a non-total ORDER BY + OFFSET can drop or
    // duplicate rows ACROSS pages. organization_number is the PK — it makes the order total.
    private const string ItemsSql =
        "SELECT organization_number, company_name, sate_kommun_code, sate_kommun_name, sni_codes "
        + FromWhere
        + """

        ORDER BY company_name, organization_number
        LIMIT @limit OFFSET @offset;
        """;

    private const string CountSql = "SELECT count(*) " + FromWhere + ";";

    public async ValueTask<PagedResult<CompanyBrowseResult>> BrowseAsync(
        CompanyBrowseCriteria criteria, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        // Separate count query BEFORE pagination (CLAUDE.md §3.6). Same FromWhere, same bound values.
        int totalCount;
        await using (var countCmd = BuildCountCommand(connection, criteria.Criteria))
        {
            var scalar = await countCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            totalCount = Convert.ToInt32(scalar, System.Globalization.CultureInfo.InvariantCulture);
        }

        var items = new List<CompanyBrowseResult>();
        await using (var itemsCmd = BuildItemsCommand(
            connection, criteria.Criteria, criteria.Page, criteria.PageSize))
        {
            await using var reader = await itemsCmd
                .ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                items.Add(new CompanyBrowseResult(
                    OrganizationNumber: reader.GetString(0),
                    Name: reader.GetString(1),
                    SeatMunicipalityCode: reader.GetString(2),
                    SeatMunicipalityName: await reader.IsDBNullAsync(3, cancellationToken).ConfigureAwait(false)
                        ? null
                        : reader.GetString(3),
                    SniCodes: reader.GetFieldValue<string[]>(4)));
            }
        }

        return new PagedResult<CompanyBrowseResult>(
            items, totalCount, criteria.Page, criteria.PageSize);
    }

    /// <summary>
    /// The page query, exactly as production emits it. <c>internal</c> so
    /// <c>CompanyWatchBrowseQueryPlanTests</c> can EXPLAIN THIS command rather than a hand-typed
    /// lookalike — the caller prefixes <c>"EXPLAIN "</c> onto <see cref="NpgsqlCommand.CommandText"/>,
    /// so production carries no diagnostic code path of its own.
    /// </summary>
    internal static NpgsqlCommand BuildItemsCommand(
        NpgsqlConnection connection, CompanyWatchCriteriaSpec spec, int page, int pageSize)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = ItemsSql;
        BindPredicate(cmd, spec);
        cmd.Parameters.AddWithValue("@limit", NpgsqlDbType.Integer, pageSize);
        cmd.Parameters.AddWithValue("@offset", NpgsqlDbType.Integer, (page - 1) * pageSize);
        return cmd;
    }

    /// <summary>The count query, exactly as production emits it. See <see cref="BuildItemsCommand"/>.</summary>
    internal static NpgsqlCommand BuildCountCommand(
        NpgsqlConnection connection, CompanyWatchCriteriaSpec spec)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = CountSql;
        BindPredicate(cmd, spec);
        return cmd;
    }

    /// <summary>
    /// Binds the predicate's parameters. Shared by both commands: SPOT'ing the WHERE *text* alone is
    /// only half the guarantee — a count that bound different VALUES than the page would report a
    /// silently wrong total with an identical predicate.
    /// </summary>
    private static void BindPredicate(NpgsqlCommand cmd, CompanyWatchCriteriaSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        // A spec rehydrated from a corrupt row can carry an EMPTY axis: CompanyWatchCriteriaSpec.Create
        // forbids it (Fork B1 — SNI AND kommun both required), but FromTrusted (which the aggregate's
        // Criteria getter uses) does not re-validate, by design. In SQL an empty axis is not an error:
        // `sni_codes && '{}'` is FALSE and `= ANY('{}')` is FALSE, so the browse would return ZERO rows
        // and look like an honest "no companies match". A silent miss is this product's cardinal sin —
        // fail loud instead. (Do NOT copy ScbCompanyRegisterStore's "bind an explicit empty text[]"
        // defense: there an empty array correctly degenerates to a no-op; here it is a wrong ANSWER.)
        if (spec.SniCodes.Count == 0 || spec.MunicipalityCodes.Count == 0)
        {
            throw new InvalidOperationException(
                "CompanyWatchCriteriaSpec har en tom axel (SNI eller kommun) — en browse mot en tom "
                + "axel returnerar tyst noll rader i stället för att fela. Kriteriet är korrupt.");
        }

        // nameof, not a 'Active' literal (§5 magic strings) and not .ToString(): the status column is
        // persisted BY NAME (HasConversion<string>()), so nameof is compile-time exact — rename the
        // enum member and the compiler forces the confrontation with the data migration.
        cmd.Parameters.AddWithValue(
            "@status", NpgsqlDbType.Text, nameof(CompanyRegisterStatus.Active));

        // text[] parameters, the ScbCompanyRegisterStore idiom. .ToArray() is deliberate —
        // IReadOnlyList<string> does not bind reliably to text[].
        cmd.Parameters.Add(new NpgsqlParameter("@kommun", NpgsqlDbType.Array | NpgsqlDbType.Text)
        {
            Value = spec.MunicipalityCodes.ToArray(),
        });
        cmd.Parameters.Add(new NpgsqlParameter("@sni", NpgsqlDbType.Array | NpgsqlDbType.Text)
        {
            Value = spec.SniCodes.ToArray(),
        });
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }
}
