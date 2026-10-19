using System.IO;
using System.Text.RegularExpressions;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// ADR 0160 D3 — an ended ad and an erased ad give the public surface the identical 410, and that
/// holds only while both rows stay in <c>job_ads</c>. A purge that deleted old archived rows would
/// turn an ended id into a 404 while an erased one still answered 410, and the difference would
/// mark the erased ads. No path in <c>src/</c> hard-deletes a job ad today; this scan keeps it so.
/// </summary>
/// <remarks>
/// It reads source text, so it sees the EF and raw-SQL forms below and not a <c>db.Remove(entity)</c>
/// on an untyped variable. <c>job_ads</c> is the dependent of no foreign key (its only relationships
/// are its owned types), so no database cascade deletes a row either.
/// </remarks>
public class JobAdHardDeleteGuardTests
{
    [Fact]
    public void No_source_file_hard_deletes_a_job_ad()
    {
        var src = Path.Combine(RepoRoot(), "src");

        var offenders = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(file => HardDeletesAJobAd(StripComments(File.ReadAllText(file))))
            .Select(file => Path.GetRelativePath(src, file))
            .ToList();

        offenders.ShouldBeEmpty(
            "A job ad must never be hard-deleted: the public read answers an ended and an erased ad with "
            + "the identical 410 (ADR 0160 D3), and a deleted ended ad would become a 404 beside the erased "
            + "ones' 410, which marks every ad someone asked to have erased. Retire an ad by Archive(); "
            + "honour an Art. 17 request by Erase(). Offending files: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Hard_delete_scan_sees_the_forms_it_guards_against()
    {
        HardDeletesAJobAd("db.JobAds.Remove(jobAd);").ShouldBeTrue();
        HardDeletesAJobAd("db.JobAds.RemoveRange(old);").ShouldBeTrue();
        HardDeletesAJobAd("""
            await db.JobAds
                .Where(j => j.Status == JobAdStatus.Archived)
                .ExecuteDeleteAsync(ct);
            """).ShouldBeTrue("a chained bulk delete spans lines");
        HardDeletesAJobAd("await db.Set<JobAd>().Where(j => j.Id == id).ExecuteDelete();").ShouldBeTrue();
        HardDeletesAJobAd("await db.Database.ExecuteSqlAsync($\"DELETE FROM job_ads WHERE id = {id}\");")
            .ShouldBeTrue();
        HardDeletesAJobAd("migrationBuilder.Sql(\"TRUNCATE TABLE job_ads;\");").ShouldBeTrue();
        HardDeletesAJobAd("db.Set<JobAd>().Remove(ad);").ShouldBeTrue();
        HardDeletesAJobAd("db.Set<JobAd>().RemoveRange(ads);").ShouldBeTrue();
        HardDeletesAJobAd("""
            var batch = db.JobAds.Where(j => j.Status == JobAdStatus.Archived).Take(500);
            await batch.ExecuteDeleteAsync(ct);
            """).ShouldBeTrue("a bulk delete split through a variable");
        HardDeletesAJobAd("""
            await db.Database.ExecuteSqlRawAsync("DELETE FROM \"public\".\"job_ads\" WHERE id = @p0", id);
            """).ShouldBeTrue("a quoted schema and table");

        HardDeletesAJobAd("db.SavedJobAds.Remove(saved);").ShouldBeFalse("a bookmark is not the ad");
        HardDeletesAJobAd("await NewCriterionAdSet.HitsWhoseAdLeftActive(db, userId).ExecuteDeleteAsync(ct);")
            .ShouldBeFalse("the Branschbevakning purge deletes hits, not ads (ADR 0175 D10)");
        HardDeletesAJobAd("migrationBuilder.Sql(\"DELETE FROM saved_job_ads;\");").ShouldBeFalse();
        HardDeletesAJobAd("await db.JobAds.Where(j => j.Id == id).ExecuteUpdateAsync(s => s, ct);")
            .ShouldBeFalse("an update is not a delete");
        HardDeletesAJobAd(StripComments("// never call db.JobAds.Remove(jobAd) here")).ShouldBeFalse(
            "a comment describing the forbidden call is not the forbidden call");
    }

    private const string JobAdSet = @"(?:\bJobAds\b|\bSet<JobAd>\(\))";

    private static bool HardDeletesAJobAd(string code) =>
        Regex.IsMatch(code, JobAdSet + @"\s*\.\s*Remove(?:Range)?\s*\(")
        || Regex.IsMatch(code, JobAdSet + @"[^;]*?\.ExecuteDelete(?:Async)?\s*\(")
        || Regex.IsMatch(code, @"\b(\w+)\s*=\s*[^;]*?" + JobAdSet
            + @"[^;]*;[\s\S]*?\b\1\s*(?:\.[^;]*?)?\.ExecuteDelete(?:Async)?\s*\(")
        || Regex.IsMatch(code, @"\b(?:DELETE\s+FROM|TRUNCATE(?:\s+TABLE)?)\s+(?:[\\""]*\w+[\\""]*\.)?[\\""]*job_ads\b",
            RegexOptions.IgnoreCase);

    // Remove line, block and XML-doc comments while leaving string literals intact.
    private static string StripComments(string source) =>
        Regex.Replace(
            source,
            @"(?<comment>//[^\n]*|/\*[\s\S]*?\*/)|(?<keep>@?""(?:[^""\\\n]|\\.|"""")*""|""""""[\s\S]*?"""""")",
            m => m.Groups["keep"].Success ? m.Groups["keep"].Value : " ");

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Jobbliggaren.sln")))
            dir = dir.Parent;

        dir.ShouldNotBeNull("could not locate the repo root (Jobbliggaren.sln) from the test output dir");
        return dir!.FullName;
    }
}
