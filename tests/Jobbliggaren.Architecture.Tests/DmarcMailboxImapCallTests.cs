using Jobbliggaren.Infrastructure.Mailbox.Dmarc;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #1981 PR 4a (ADR 0185; senior-cto-advisor r1 mechanism 6–8 and r2 §1; security-auditor rulings 1 and 5, B1, D2–D4)
/// — what the mailbox adapter may ask MailKit and MimeKit to do, read from its IL. Anyone can mail the report address,
/// so the adapter reads only bounded, server-truncated ranges, opens the inbox read-only, never searches, never lets a
/// MIME structure parser see a message, signs in with one constant mechanism, and leaves certificate validation and
/// protocol logging at MailKit's defaults. The scripted-server tests measure the commands on the wire; these pin the
/// calls that produce them, including calls no scripted run happens to reach.
/// </summary>
public class DmarcMailboxImapCallTests
{
    private const string Namespace = "Jobbliggaren.Infrastructure.Mailbox.Dmarc";

    private static readonly string MailboxAssemblyPath = typeof(DmarcMailboxCollectionExtensions).Assembly.Location;

    /// <summary>Every MailKit member the adapter was reviewed for. The allowlist is closed: a new call fails here first.</summary>
    private static readonly string[] ReviewedMailKitMembers =
    [
        "MailKit.FetchRequest::.ctor(MailKit.MessageSummaryItems)",
        "MailKit.IMailFolder::FetchAsync(System.Int32,System.Int32,MailKit.IFetchRequest,System.Threading.CancellationToken)",
        "MailKit.IMailFolder::GetStreamAsync(MailKit.UniqueId,System.String,System.Int32,System.Int32,System.Threading.CancellationToken,MailKit.ITransferProgress)",
        "MailKit.IMailFolder::OpenAsync(MailKit.FolderAccess,System.Threading.CancellationToken)",
        "MailKit.IMailFolder::get_Count()",
        "MailKit.IMailFolder::get_UidValidity()",
        "MailKit.IMessageSummary::get_Index()",
        "MailKit.IMessageSummary::get_InternalDate()",
        "MailKit.IMessageSummary::get_Size()",
        "MailKit.IMessageSummary::get_UniqueId()",
        "MailKit.MailService::AuthenticateAsync(MailKit.Security.SaslMechanism,System.Threading.CancellationToken)",
        "MailKit.MailService::ConnectAsync(System.IO.Stream,System.String,System.Int32,MailKit.Security.SecureSocketOptions,System.Threading.CancellationToken)",
        "MailKit.MailService::ConnectAsync(System.String,System.Int32,MailKit.Security.SecureSocketOptions,System.Threading.CancellationToken)",
        "MailKit.MailService::DisconnectAsync(System.Boolean,System.Threading.CancellationToken)",
        "MailKit.MailService::Dispose()",
        "MailKit.MailService::get_AuthenticationMechanisms()",
        "MailKit.MailService::get_IsConnected()",
        "MailKit.MailService::set_Timeout(System.Int32)",
        "MailKit.MailStore::get_Inbox()",
        "MailKit.Net.Imap.ImapClient::.ctor()",
        "MailKit.Net.Imap.ImapCommandException::get_Response()",
        "MailKit.Security.SaslMechanismPlain::.ctor(System.String,System.String)",
        "MailKit.UniqueId::.ctor(System.UInt32,System.UInt32)",
        "MailKit.UniqueId::get_Id()",
        "MailKit.UniqueId::get_IsValid()",
    ];

    /// <summary>
    /// Every MimeKit member the adapter was reviewed for: the two header-value parsers, each given one unfolded field
    /// value, and the two transfer decoders (security-auditor re-check, Major 3 (a)). Closed, as above.
    /// </summary>
    private static readonly string[] ReviewedMimeKitMembers =
    [
        "MimeKit.ContentDisposition::TryParse(System.Byte[],MimeKit.ContentDisposition&)",
        "MimeKit.ContentDisposition::get_FileName()",
        "MimeKit.ContentType::IsMimeType(System.String,System.String)",
        "MimeKit.ContentType::TryParse(System.Byte[],MimeKit.ContentType&)",
        "MimeKit.ContentType::get_MediaType()",
        "MimeKit.ContentType::get_Name()",
        "MimeKit.ContentType::get_Parameters()",
        "MimeKit.Encodings.Base64Decoder::.ctor()",
        "MimeKit.Encodings.IMimeDecoder::Decode(System.Byte[],System.Int32,System.Int32,System.Byte[])",
        "MimeKit.Encodings.IMimeDecoder::EstimateOutputLength(System.Int32)",
        "MimeKit.Encodings.QuotedPrintableDecoder::.ctor()",
        "MimeKit.ParameterList::TryGetValue(System.String,System.String&)",
    ];

    private static readonly string[] FolderTypes = ["MailKit.IMailFolder", "MailKit.MailFolder", "MailKit.Net.Imap.ImapFolder", "MailKit.IMailFolderExtensions"];

    private static readonly string[] ClientTypes = ["MailKit.MailService", "MailKit.MailStore", "MailKit.IMailService", "MailKit.IMailStore", "MailKit.Net.Imap.ImapClient", "MailKit.Net.Imap.IImapClient"];

    private static readonly string[] SummaryTypes = ["MailKit.IMessageSummary", "MailKit.MessageSummary"];

    /// <summary>
    /// The never-list, one entry per thing the rulings forbid by name. The closed allowlists above already refuse all
    /// of it; this states each prohibition on its own so that widening an allowlist cannot quietly admit one.
    /// </summary>
    private static readonly Dictionary<string, Func<MemberReference, bool>> Bans = new()
    {
        ["SEARCH, UID SEARCH and ESEARCH"] = member => On(member, FolderTypes) && member.Name.StartsWith("Search", StringComparison.Ordinal),
        ["SORT"] = member => On(member, FolderTypes) && member.Name.StartsWith("Sort", StringComparison.Ordinal),
        ["THREAD"] = member => On(member, FolderTypes) && member.Name is "Thread" or "ThreadAsync",
        ["STORE"] = member => On(member, FolderTypes) && member.Name.StartsWith("Store", StringComparison.Ordinal),
        ["MOVE"] = member => On(member, FolderTypes) && member.Name.StartsWith("MoveTo", StringComparison.Ordinal),
        ["COPY"] = member => On(member, FolderTypes) && member.Name.StartsWith("CopyTo", StringComparison.Ordinal),
        ["EXPUNGE"] = member => On(member, FolderTypes) && member.Name.StartsWith("Expunge", StringComparison.Ordinal),
        ["CLOSE"] = member => On(member, FolderTypes) && member.Name.StartsWith("Close", StringComparison.Ordinal),
        ["APPEND and REPLACE"] = member => On(member, FolderTypes)
            && (member.Name.StartsWith("Append", StringComparison.Ordinal) || member.Name.StartsWith("Replace", StringComparison.Ordinal)),
        ["IDLE, NOOP, CHECK and STATUS"] = member => (On(member, FolderTypes) || On(member, ClientTypes))
            && (member.Name.StartsWith("Idle", StringComparison.Ordinal) || member.Name.StartsWith("NoOp", StringComparison.Ordinal)
                || member.Name.StartsWith("Check", StringComparison.Ordinal) || member.Name.StartsWith("Status", StringComparison.Ordinal)),
        ["A folder other than the inbox"] = member => (On(member, FolderTypes) || On(member, ClientTypes))
            && (member.Name.StartsWith("GetFolder", StringComparison.Ordinal) || member.Name.StartsWith("GetSubfolder", StringComparison.Ordinal)),
        ["GetMessage"] = member => On(member, FolderTypes) && member.Name.StartsWith("GetMessage", StringComparison.Ordinal),
        ["GetBodyPart"] = member => On(member, FolderTypes) && member.Name.StartsWith("GetBodyPart", StringComparison.Ordinal),
        ["GetHeaders, which parses with MimeKit"] = member => On(member, FolderTypes) && member.Name.StartsWith("GetHeaders", StringComparison.Ordinal),
        ["A stream that is not a partial range of one section"] = member => On(member, FolderTypes)
            && member.Name.StartsWith("GetStream", StringComparison.Ordinal)
            && !HasParameters(member, "MailKit.UniqueId", "System.String", "System.Int32", "System.Int32"),
        ["A fetch by UID set or index list"] = member => On(member, FolderTypes)
            && member.Name.StartsWith("Fetch", StringComparison.Ordinal)
            && !HasParameters(member, "System.Int32", "System.Int32", "MailKit.IFetchRequest", "System.Threading.CancellationToken"),
        ["BODYSTRUCTURE, ENVELOPE, PreviewText and headers off a summary"] = member => On(member, SummaryTypes)
            && member.Name is "get_Body" or "get_BodyParts" or "get_Envelope" or "get_PreviewText" or "get_Headers"
                or "get_Attachments" or "get_TextBody" or "get_HtmlBody" or "get_References",
        ["UniqueId.MaxValue, which serialises as *"] = member => member is FieldReference && On(member, ["MailKit.UniqueId"]) && member.Name == "MaxValue",
        ["UniqueIdRange, UniqueIdRange.All and UniqueIdSet"] = member => On(member, ["MailKit.UniqueIdRange", "MailKit.UniqueIdSet"]),
        ["MimeMessage.Load, MimeEntity.Load and HeaderList.Load"] = member => On(member, ["MimeKit.MimeMessage", "MimeKit.MimeEntity", "MimeKit.HeaderList"])
            && member.Name.StartsWith("Load", StringComparison.Ordinal),
        ["A MIME structure parser"] = member => On(member, ["MimeKit.MimeParser", "MimeKit.ExperimentalMimeParser", "MimeKit.MimeReader"]),
        ["The protocol logger, which writes the AUTHENTICATE exchange"] = member => On(member, ["MailKit.ProtocolLogger"])
            || (On(member, ClientTypes) && member.Name == "set_ProtocolLogger")
            || (member is MethodReference { Name: ".ctor" } constructor && On(member, ClientTypes)
                && constructor.Parameters.Any(parameter => parameter.ParameterType.FullName == "MailKit.IProtocolLogger")),
        ["A certificate validation callback, revocation or protocol setting"] = member => On(member, ClientTypes)
            && member.Name is "set_ServerCertificateValidationCallback" or "set_CheckCertificateRevocation" or "set_SslProtocols",
        ["The credential overload, which falls back through every mechanism and LOGIN"] = member => On(member, ClientTypes)
            && member.Name.StartsWith("Authenticate", StringComparison.Ordinal)
            && member is MethodReference method
            && method.Parameters.FirstOrDefault()?.ParameterType.FullName != "MailKit.Security.SaslMechanism",
    };

    public static TheoryData<string> BanNames() => [.. Bans.Keys];

    [Fact]
    public void The_adapter_calls_only_the_mailkit_members_it_was_reviewed_for()
    {
        AdapterReferences().Select(reference => reference.Key).Where(key => key.StartsWith("MailKit.", StringComparison.Ordinal))
            .Distinct().ShouldBe(ReviewedMailKitMembers, ignoreOrder: true);
    }

    [Fact]
    public void The_adapter_calls_only_the_mimekit_members_it_was_reviewed_for()
    {
        AdapterReferences().Select(reference => reference.Key).Where(key => key.StartsWith("MimeKit.", StringComparison.Ordinal))
            .Distinct().ShouldBe(ReviewedMimeKitMembers, ignoreOrder: true);
    }

    [Theory]
    [MemberData(nameof(BanNames))]
    public void The_adapter_never_names_a_banned_member(string ban)
    {
        AdapterReferences().Where(reference => Bans[ban](reference.Member))
            .Select(reference => $"{reference.Owner}: {reference.Key}")
            .ShouldBeEmpty($"{ban} is never sent or called by the DMARC mailbox adapter (ADR 0185)");
    }

    /// <summary>A ban that names no member MailKit or MimeKit has is a typo that bans nothing.</summary>
    [Theory]
    [MemberData(nameof(BanNames))]
    public void Every_ban_names_a_member_the_libraries_have(string ban)
    {
        LibraryMembers().ShouldContain(member => Bans[ban](member), $"'{ban}' matches nothing in MailKit or MimeKit");
    }

    /// <summary>The scans above read real calls in the adapter's own types, compiler-generated code mapped back.</summary>
    [Fact]
    public void The_scan_reads_the_adapters_own_calls()
    {
        var owners = AdapterReferences().GroupBy(reference => reference.Owner)
            .ToDictionary(group => group.Key, group => group.Select(reference => reference.Key).ToHashSet());

        owners[$"{Namespace}.ImapDmarcMailboxSession"].ShouldContain(ReviewedMailKitMembers[2]);
        owners[$"{Namespace}.ImapDmarcMailbox"].ShouldContain(ReviewedMailKitMembers[10]);
        owners[$"{Namespace}.ReportPartRules"].ShouldContain(ReviewedMimeKitMembers[3]);
    }

    /// <summary>EXAMINE, never SELECT: every open passes the read-only access as a constant (CTO r1 mechanism 7).</summary>
    [Fact]
    public void The_inbox_is_only_ever_opened_read_only()
    {
        var readOnly = EnumConstant("MailKit.FolderAccess", "ReadOnly");
        var opens = AdapterCalls().Where(call => On(call.Method, FolderTypes) && call.Method.Name.StartsWith("Open", StringComparison.Ordinal)).ToList();

        opens.ShouldNotBeEmpty();
        opens.ShouldAllBe(call => call.Method.Parameters.Count == 2 && ConstantsBefore(call.Instruction).FirstOrDefault() as int? == readOnly,
            "the access argument is FolderAccess.ReadOnly, loaded as a constant just before the token");
    }

    /// <summary>
    /// Every fetch request asks for the UID, the size and the internal date at most: never a structure, an envelope, a
    /// preview or a header, which would carry what a sender wrote into the listing (security-auditor ruling 5).
    /// </summary>
    [Fact]
    public void Every_fetch_request_asks_for_metadata_only()
    {
        var allowed = EnumConstant("MailKit.MessageSummaryItems", "UniqueId")
            | EnumConstant("MailKit.MessageSummaryItems", "Size")
            | EnumConstant("MailKit.MessageSummaryItems", "InternalDate");
        var requests = AdapterCalls().Where(call => call.Instruction.OpCode == OpCodes.Newobj && On(call.Method, ["MailKit.FetchRequest"])).ToList();

        var items = requests.Select(call => ConstantOf(call.Instruction.Previous)).ToList();

        items.Count.ShouldBe(2, "the probe and the window");
        items.ShouldAllBe(item => item.HasValue && (item.Value & ~allowed) == 0);
    }

    /// <summary>Production connects to the constant host on 993 with implicit TLS; only a test builds the stream seam.</summary>
    [Fact]
    public void Production_connects_only_to_the_constant_host_over_implicit_tls()
    {
        var sslOnConnect = EnumConstant("MailKit.Security.SecureSocketOptions", "SslOnConnect");
        var connects = AdapterCalls()
            .Where(call => On(call.Method, ClientTypes) && call.Method.Name == "ConnectAsync" && call.Method.Parameters[0].ParameterType.FullName == "System.String")
            .ToList();

        var connect = connects.ShouldHaveSingleItem();
        ConstantsBefore(connect.Instruction).Take(3).ShouldBe([sslOnConnect, 993, "imap.strato.de"],
            "the socket options, the port and the host, nearest first");

        AdapterCalls().Where(call => call.Instruction.OpCode == OpCodes.Newobj && call.Method.DeclaringType.FullName == $"{Namespace}.ImapStreamConnector")
            .ShouldBeEmpty("nothing in production code builds the test seam");
    }

    /// <summary>Security-auditor B1: one SASL exchange with a constant mechanism, and that mechanism is PLAIN.</summary>
    [Fact]
    public void Sign_in_is_one_constant_sasl_mechanism()
    {
        var authentications = AdapterCalls().Where(call => On(call.Method, ClientTypes) && call.Method.Name.StartsWith("Authenticate", StringComparison.Ordinal)).ToList();
        var mechanisms = AdapterCalls()
            .Where(call => call.Instruction.OpCode == OpCodes.Newobj && call.Method.DeclaringType.FullName.StartsWith("MailKit.Security.SaslMechanism", StringComparison.Ordinal))
            .Select(call => call.Method.DeclaringType.FullName)
            .ToList();

        authentications.ShouldHaveSingleItem().Method.Parameters[0].ParameterType.FullName.ShouldBe("MailKit.Security.SaslMechanism");
        mechanisms.ShouldBe(["MailKit.Security.SaslMechanismPlain"]);
    }

    /// <summary>
    /// CTO r2 §1: the message count is read once, from the EXAMINE response, and is the bound for the whole listing.
    /// MailKit moves it on every untagged EXISTS, so the session never reads it again.
    /// </summary>
    [Fact]
    public void Only_the_open_reads_the_message_count()
    {
        AdapterCalls().Where(call => On(call.Method, FolderTypes) && call.Method.Name == "get_Count")
            .Select(call => call.Owner).Distinct()
            .ShouldBe([$"{Namespace}.ImapDmarcMailbox"]);
    }

    private static List<AdapterCall> AdapterCalls()
    {
        using var assembly = AssemblyDefinition.ReadAssembly(MailboxAssemblyPath);
        var calls = new List<AdapterCall>();
        foreach (var type in assembly.MainModule.GetTypes())
        {
            var owner = ProductionTypeSweep.OwnerOf(type).FullName;
            foreach (var method in type.Methods.Where(method => method.HasBody))
            {
                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.Operand is MethodReference called)
                        calls.Add(new AdapterCall(owner, instruction, called is GenericInstanceMethod generic ? generic.ElementMethod : called));
                }
            }
        }

        return calls;
    }

    private static List<AdapterReference> AdapterReferences()
    {
        using var assembly = AssemblyDefinition.ReadAssembly(MailboxAssemblyPath);
        var references = new List<AdapterReference>();
        foreach (var type in assembly.MainModule.GetTypes())
        {
            var owner = ProductionTypeSweep.OwnerOf(type).FullName;
            foreach (var method in type.Methods.Where(method => method.HasBody))
            {
                foreach (var instruction in method.Body.Instructions)
                {
                    MemberReference? member = instruction.Operand switch
                    {
                        GenericInstanceMethod generic => generic.ElementMethod,
                        MethodReference called => called,
                        FieldReference field => field,
                        _ => null,
                    };
                    if (member is not null && IsLibrary(member))
                        references.Add(new AdapterReference(owner, member, KeyOf(member)));
                }
            }
        }

        return references;
    }

    /// <summary>Every public method and field MailKit and MimeKit define, read from the copies the Worker ships.</summary>
    private static List<MemberReference> LibraryMembers()
    {
        var members = new List<MemberReference>();
        foreach (var library in new[] { "MailKit.dll", "MimeKit.dll" })
        {
            using var assembly = AssemblyDefinition.ReadAssembly(Path.Combine(Path.GetDirectoryName(MailboxAssemblyPath)!, library));
            foreach (var type in assembly.MainModule.GetTypes().Where(type => type.IsPublic || type.IsNestedPublic))
            {
                members.AddRange(type.Methods.Where(method => method.IsPublic));
                members.AddRange(type.Fields.Where(field => field.IsPublic));
            }
        }

        return members;
    }

    private static int EnumConstant(string enumType, string member)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(Path.Combine(Path.GetDirectoryName(MailboxAssemblyPath)!, "MailKit.dll"));
        var field = assembly.MainModule.GetType(enumType).Fields.Single(field => field.Name == member);
        return Convert.ToInt32(field.Constant, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>The constants loaded in the few instructions before a call, nearest first: its literal arguments.</summary>
    private static List<object> ConstantsBefore(Instruction call)
    {
        var constants = new List<object>();
        var instruction = call.Previous;
        for (var step = 0; instruction is not null && step < 6; step++, instruction = instruction.Previous)
        {
            if (instruction.OpCode == OpCodes.Ldstr)
                constants.Add((string)instruction.Operand);
            else if (ConstantOf(instruction) is { } value)
                constants.Add(value);
        }

        return constants;
    }

    private static int? ConstantOf(Instruction? instruction) => instruction?.OpCode.Code switch
    {
        Code.Ldc_I4_M1 => -1,
        Code.Ldc_I4_0 => 0,
        Code.Ldc_I4_1 => 1,
        Code.Ldc_I4_2 => 2,
        Code.Ldc_I4_3 => 3,
        Code.Ldc_I4_4 => 4,
        Code.Ldc_I4_5 => 5,
        Code.Ldc_I4_6 => 6,
        Code.Ldc_I4_7 => 7,
        Code.Ldc_I4_8 => 8,
        Code.Ldc_I4_S => (sbyte)instruction.Operand,
        Code.Ldc_I4 => (int)instruction.Operand,
        _ => null,
    };

    private static bool IsLibrary(MemberReference member) =>
        OutermostNamespace(member.DeclaringType) is var space
        && (space == "MailKit" || space.StartsWith("MailKit.", StringComparison.Ordinal)
            || space == "MimeKit" || space.StartsWith("MimeKit.", StringComparison.Ordinal));

    private static bool On(MemberReference member, string[] declaringTypes) => declaringTypes.Contains(member.DeclaringType.FullName);

    private static bool HasParameters(MemberReference member, params string[] leading) =>
        member is MethodReference method
        && method.Parameters.Count >= leading.Length
        && leading.Select((type, index) => method.Parameters[index].ParameterType.FullName == type).All(matches => matches);

    private static string KeyOf(MemberReference member) => member switch
    {
        MethodReference method => $"{method.DeclaringType.FullName}::{method.Name}({string.Join(",", method.Parameters.Select(parameter => parameter.ParameterType.FullName))})",
        _ => $"{member.DeclaringType.FullName}::{member.Name}",
    };

    private static string OutermostNamespace(TypeReference type)
    {
        while (type.DeclaringType is not null)
            type = type.DeclaringType;
        return type.Namespace;
    }

    private sealed record AdapterCall(string Owner, Instruction Instruction, MethodReference Method);

    private sealed record AdapterReference(string Owner, MemberReference Member, string Key);
}
