using System.IO.Compression;
using Jobbliggaren.Infrastructure.Common.IO;
using Jobbliggaren.Infrastructure.Email.Dmarc;
using Jobbliggaren.Infrastructure.Email.Dmarc.Decoding;
using Mono.Cecil;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #1981 PR 4a (ADR 0185) — a report part a sender wrote is read in memory and never written to disk: no type in the
/// DMARC infrastructure, nor the bounded-read helpers its decoder reads through, names a file, a directory, a zip file
/// on disk or a temporary path. The sites are the IL's own, by owning type.
/// </summary>
public class DmarcReportDiskBoundaryTests
{
    private static readonly string[] Namespaces = ["Jobbliggaren.Infrastructure.Email.Dmarc", "Jobbliggaren.Infrastructure.Mailbox.Dmarc"];

    private const string MailboxSession = "Jobbliggaren.Infrastructure.Mailbox.Dmarc.ImapDmarcMailboxSession";

    private const string Bound = "ADR 0185: a sender's report part is read in memory only, never written to disk.";

    private static readonly string[] DecoderHelpers =
    [
        typeof(ByteBudget).FullName!,
        typeof(ByteCappedReadStream).FullName!,
        typeof(HardenedXmlReaderSettings).FullName!,
    ];

    private static readonly string[] DiskTypes =
    [
        typeof(File).FullName!,
        typeof(FileInfo).FullName!,
        typeof(FileStream).FullName!,
        typeof(Directory).FullName!,
        typeof(DirectoryInfo).FullName!,
        typeof(ZipFile).FullName!,
        typeof(ZipFileExtensions).FullName!,
    ];

    public static TheoryData<string> DiskTypeNames() => [.. DiskTypes];

    [Theory]
    [MemberData(nameof(DiskTypeNames))]
    public void No_dmarc_report_type_names_a_disk_type(string diskType)
    {
        ProductionTypeSweep.OwnersReferencing(diskType).Where(InScope).ShouldBeEmpty(Bound);
    }

    [Fact]
    public void No_dmarc_report_type_asks_for_a_temporary_path()
    {
        CallsInScope()
            .Where(call => call.Method.DeclaringType.FullName == typeof(Path).FullName
                && call.Method.Name.StartsWith("GetTemp", StringComparison.Ordinal))
            .Select(call => $"{call.Owner}: {call.Method.Name}")
            .ShouldBeEmpty(Bound);
    }

    /// <summary>The scans above reach the decoder, its helpers and the store, and read their real references.</summary>
    [Fact]
    public void The_scans_reach_the_decoder_and_read_its_calls()
    {
        var owners = ProductionTypeSweep.Types().Select(type => ProductionTypeSweep.OwnerOf(type).FullName).Where(InScope).ToHashSet();
        owners.ShouldContain(typeof(XmlDmarcReportDecoder).FullName!);
        owners.ShouldContain(typeof(SqlDmarcReportStore).FullName!);
        owners.ShouldContain(typeof(ByteCappedReadStream).FullName!);
        owners.ShouldContain(MailboxSession);

        ProductionTypeSweep.OwnersReferencing(typeof(ZipArchive).FullName!).ShouldContain(typeof(XmlDmarcReportDecoder).FullName!);
        CallsInScope().ShouldContain(call => call.Owner == typeof(XmlDmarcReportDecoder).FullName
            && call.Method.DeclaringType.FullName == typeof(MemoryStream).FullName && call.Method.Name == ".ctor");
        CallsInScope().ShouldContain(call => call.Owner == MailboxSession && call.Method.Name == "GetStreamAsync");
        typeof(Path).GetMethods().ShouldContain(method => method.Name.StartsWith("GetTemp", StringComparison.Ordinal));
    }

    private static bool InScope(string owner) =>
        Namespaces.Any(scope => owner.StartsWith(scope + ".", StringComparison.Ordinal)) || DecoderHelpers.Contains(owner);

    private static List<ScopedCall> CallsInScope()
    {
        var calls = new List<ScopedCall>();
        foreach (var production in ProductionAssemblies.All)
        {
            using var assembly = production.ReadDefinition();
            foreach (var type in assembly.MainModule.GetTypes())
            {
                var owner = ProductionTypeSweep.OwnerOf(type).FullName;
                if (!InScope(owner))
                    continue;

                foreach (var method in type.Methods.Where(method => method.HasBody))
                {
                    foreach (var instruction in method.Body.Instructions)
                    {
                        if (instruction.Operand is MethodReference called)
                            calls.Add(new ScopedCall(owner, called is GenericInstanceMethod generic ? generic.ElementMethod : called));
                    }
                }
            }
        }

        return calls;
    }

    private sealed record ScopedCall(string Owner, MethodReference Method);
}
