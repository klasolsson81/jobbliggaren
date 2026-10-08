using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// Documentation tripwire for the supported lifecycle. It neither executes the runbook nor
/// treats prose as an oracle for production behavior; genuine API/Worker tests cover that behavior.
/// </summary>
public partial class AccountDeletionRunbookParityTests
{
    [Fact]
    public void Runbook_ShouldDescribeProtectedScheduling_WhenOperatorsHandleDeletionRequests()
    {
        var runbook = Normalised(ReadRunbook());

        runbook.ShouldContain("POST /api/v1/admin/accounts/{id}/deletion");
        runbook.ShouldContain("Admin session and administrator's own inbox grant");
        runbook.ShouldContain("use normal admin scheduling and the administrator's own inbox code");
    }

    [Fact]
    public void Runbook_ShouldRetireRestoreAndDirectLifecycleDml_WhenRead()
    {
        var runbook = ReadRunbook();
        var normalised = Normalised(runbook);

        normalised.ShouldContain("Restore is unavailable.");
        normalised.ShouldContain("Do not clear `deleted_at`");
        normalised.ShouldContain("do not substitute direct profile or Identity SQL for the lifecycle protocol");
        ContainsExecutableLifecycleDml(runbook).ShouldBeFalse(
            "Scheduling and restore instructions must use the protected lifecycle, not executable SQL bypasses.");
    }

    [Theory]
    [InlineData("sql", "UPDATE job_seekers SET deleted_at = NOW();")]
    [InlineData("SQL", "uPdAtE\n  public . job_seekers\nSET deleted_at = null;")]
    [InlineData("postgresql", "UPDATE \"public\" . \"job_seekers\" SET \"deleted_at\" = NULL;")]
    [InlineData("psql", "UPDATE \"identity\".\"AspNetUsers\" SET access_revision = access_revision + 1;")]
    [InlineData("sql", "WITH deleted AS (UPDATE job_seekers SET deleted_at = NOW() RETURNING user_id) SELECT user_id FROM deleted;")]
    [InlineData("bash", "psql -c 'UPDATE public.job_seekers SET deleted_at = NULL WHERE id = :id;'")]
    [InlineData("sh", "psql <<'SQL'\nDELETE FROM identity.\"AspNetUserLogins\" WHERE user_id = :id;\nSQL")]
    [InlineData("powershell", "psql -c 'UPDATE \"public\".\"resumes\" SET \"deleted_at\" = NULL;'")]
    [InlineData("", "UPDATE ONLY job_seekers SET deleted_at = NOW();")]
    [InlineData("sql", "DELETE\n FROM \"identity\" . \"AspNetUsers\" WHERE id = :id;")]
    [InlineData("shell", "psql -c 'TRUNCATE TABLE public.job_seekers;'")]
    [InlineData("sql", "INSERT INTO job_seekers (id, user_id) VALUES (:id, :user_id);")]
    public void LifecycleDml_ShouldBeDetected_WhenExecutableFenceBypassesTheProtocol(string language, string body)
    {
        ContainsExecutableLifecycleDml($"```{language}\n{body}\n```\n").ShouldBeTrue();
    }

    [Theory]
    [InlineData("SELECT count(*) FROM job_seekers WHERE deleted_at IS NOT NULL;")]
    [InlineData("SELECT count(*) FROM identity.\"AspNetUserLogins\";")]
    public void LifecycleDml_ShouldBeAllowed_WhenSqlFenceOnlyReadsAggregates(string body)
    {
        ContainsExecutableLifecycleDml($"```sql\n{body}\n```\n").ShouldBeFalse();
    }

    [Theory]
    [InlineData("Do not execute `UPDATE job_seekers SET deleted_at = NULL`.")]
    [InlineData("```text\nHistorical UPDATE job_seekers SET deleted_at = NULL is retired.\n```\n")]
    [InlineData("```bash\ncurl -X POST /api/v1/admin/accounts/{id}/deletion\n```\n")]
    [InlineData("```sh\nsudo bash /opt/jobbliggaren/deploy/systemd/jobbliggaren-redis-account.sh\n```\n")]
    [InlineData("```sql\nUPDATE job_seekers_archive SET archived_at = NOW();\n```\n")]
    public void LifecycleDml_ShouldBeAllowed_WhenDocumentationHasNoExecutableLifecycleWrite(string markdown)
    {
        ContainsExecutableLifecycleDml(markdown).ShouldBeFalse();
    }

    [Fact]
    public void LifecycleDml_ShouldBeDetected_WhenIndentedTildeFenceContainsEscapedShellSql()
    {
        const string markdown = "  ~~~BASH\npsql -c \"UPDATE \\\"public\\\".\\\"job_seekers\\\" SET deleted_at = NULL;\"\n  ~~~\n";

        ContainsExecutableLifecycleDml(markdown).ShouldBeTrue();
    }

    private static bool ContainsExecutableLifecycleDml(string markdown)
    {
        foreach (Match fence in CodeFences().Matches(markdown))
        {
            var language = fence.Groups["language"].Value.Trim().Split([' ', '\t'])[0].ToLowerInvariant();
            if (language is not ("" or "sql" or "postgresql" or "pgsql" or "psql" or "bash" or "sh" or
                "shell" or "zsh" or "console" or "terminal" or "powershell" or "pwsh" or "ps1" or "cmd" or "bat"))
                continue;

            var body = fence.Groups["body"].Value.Replace("\\\"", "\"", StringComparison.Ordinal);
            if (LifecycleDml().IsMatch(body))
                return true;
        }

        return false;
    }

    private static string ReadRunbook() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "docs", "runbooks", "account-deletion.md"));

    private static string Normalised(string text) => Whitespace().Replace(text, " ").Trim();

    [GeneratedRegex(@"^[ \t]*(?<fence>`{3,}|~{3,})[ \t]*(?<language>[^\r\n]*)\r?\n(?<body>.*?)^[ \t]*\k<fence>[ \t]*\r?$",
        RegexOptions.Singleline | RegexOptions.Multiline)]
    private static partial Regex CodeFences();

    [GeneratedRegex("""
        \b(?:UPDATE\s+(?:ONLY\s+)?|DELETE\s+FROM\s+|INSERT\s+INTO\s+|MERGE\s+INTO\s+|TRUNCATE\s+(?:TABLE\s+)?)(?:(?:"[^"]+"|[a-z_]\w*)\s*\.\s*)?"?(?:job_seekers|AspNetUsers|AspNetUserLogins)"?(?![a-z0-9_])|\bUPDATE\b[^;]*?\bSET\b[^;]*?\bdeleted_at"?\s*=
        """, RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex LifecycleDml();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    // thisFile = <repo>/tests/Jobbliggaren.Architecture.Tests/AccountDeletionRunbookParityTests.cs → up two = repo root.
    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}
