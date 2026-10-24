using System.Globalization;
using Jobbliggaren.Application.Admin.Email.Dmarc;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Mailbox.Dmarc;

/// <summary>
/// #1981 PR 4a — the bounded listing (CTO r2 §1; security-auditor D1–D6), measured against a scripted server. Anyone can
/// mail the report address, so a flood must cost the Worker the cap's worth of metadata and a logarithmic number of
/// probes, never the mailbox's size. D6 (i)–(vi) are the facts below, one test each.
/// </summary>
public class ImapDmarcMailboxListingTests
{
    private const int Cap = DmarcIngestionLimits.PerRunMessageCap;

    [Fact]
    public async Task A_mailbox_far_larger_than_the_cap_requests_metadata_for_at_most_the_cap_D6_i()
    {
        var uids = GappedUids(new Random(1981), count: 1000);
        var cursor = (long)uids[399] + 1;
        await using var harness = new MailboxHarness(ScriptedMailbox.WithUids(uids));

        var entries = await ListAsync(harness, cursor);

        var fetches = harness.CommandsAfterExamine().Select(FetchCommand.Parse).ToList();
        var window = fetches.Where(fetch => fetch.Items.Contains("RFC822.SIZE", StringComparison.Ordinal)).ShouldHaveSingleItem();
        window.Sequences.Count.ShouldBeLessThanOrEqualTo(Cap);
        var probes = fetches.Where(fetch => fetch.Items == "UID").ToList();
        probes.ShouldAllBe(probe => probe.Sequences.Count == 1 && probe.Sequences[0] <= uids.Length);
        probes.Count.ShouldBeLessThanOrEqualTo(1 + (int)Math.Ceiling(Math.Log2(uids.Length)));
        fetches.Count.ShouldBe(probes.Count + 1, "between the first probe and the window, only non-UID FETCH is sent (D2)");
        entries.Count.ShouldBe(Cap);
    }

    [Fact]
    public async Task No_message_set_holds_a_star_and_nothing_searches_D6_ii()
    {
        var uids = GappedUids(new Random(7), count: 300);
        await using var harness = new MailboxHarness(ScriptedMailbox.WithUids(uids));

        await ListAsync(harness, uids[120]);

        harness.Server.Commands.ShouldNotContain(command => command.Contains("SEARCH", StringComparison.OrdinalIgnoreCase)
            || command.StartsWith("SORT", StringComparison.OrdinalIgnoreCase)
            || command.StartsWith("THREAD", StringComparison.OrdinalIgnoreCase));
        harness.Server.Commands.Where(IsFetch).ShouldNotContain(command => command.Contains('*', StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_window_starts_at_the_first_uid_above_the_cursor_D6_iii()
    {
        uint[] uids = [2, 3, 5, 8, 13, 21, 34, 55, 89, 144];
        await using var harness = new MailboxHarness(ScriptedMailbox.WithUids(uids));

        var entries = await ListAsync(harness, cursor: 20);

        entries.Select(entry => entry.Position).ShouldBe([21L, 34, 55, 89, 144]);
        var window = harness.CommandsAfterExamine().Select(FetchCommand.Parse).Last();
        window.Sequences.ShouldBe([6, 7, 8, 9, 10]);
    }

    [Fact]
    public async Task An_empty_mailbox_sends_no_fetch_at_all_D6_iv()
    {
        await using var harness = new MailboxHarness(ScriptedMailbox.WithUids([]));

        (await ListAsync(harness, cursor: 0)).ShouldBeEmpty();

        harness.CommandsAfterExamine().ShouldBeEmpty();
    }

    [Fact]
    public async Task An_idle_mailbox_sends_one_probe_and_no_window_D6_iv()
    {
        uint[] uids = [4, 9, 16];
        await using var harness = new MailboxHarness(ScriptedMailbox.WithUids(uids));

        (await ListAsync(harness, cursor: 16)).ShouldBeEmpty();

        harness.CommandsAfterExamine().ShouldBe(["FETCH 3 UID"]);
    }

    [Fact]
    public async Task The_listing_finds_the_first_uids_above_any_cursor_over_gapped_mailboxes_D6_v()
    {
        var random = new Random(20261024);
        for (var mailbox = 0; mailbox < 40; mailbox++)
        {
            var uids = GappedUids(random, random.Next(1, 400));
            await using var harness = new MailboxHarness(ScriptedMailbox.WithUids(uids));
            await using var session = await harness.OpenAsync();
            foreach (var cursor in Cursors(random, uids))
            {
                var max = random.Next(1, 120);
                var listing = await session.ListAfterAsync(cursor, max, TestContext.Current.CancellationToken);

                var expected = uids.Where(uid => uid > cursor).Take(max).Select(uid => (long)uid);
                listing.ShouldBeOfType<DmarcMailboxListing.Listed>().Entries.Select(entry => entry.Position)
                    .ShouldBe(expected, $"mailbox {mailbox}, {uids.Length} messages, cursor {cursor}, max {max}");
            }
        }
    }

    [Fact]
    public async Task An_untagged_exists_during_the_bisection_leaves_the_upper_bound_unchanged_D6_vi()
    {
        var uids = GappedUids(new Random(3), count: 500);
        await using var harness = new MailboxHarness(ScriptedMailbox.WithUids(uids));
        var probes = 0;
        harness.Server.Intercept = command =>
            command.EndsWith(" UID", StringComparison.Ordinal) && ++probes == 2 ? new Interception.Prepend("* 9000 EXISTS\r\n") : null;

        var entries = await ListAsync(harness, cursor: uids[^20]);

        var fetches = harness.CommandsAfterExamine().Select(FetchCommand.Parse).ToList();
        fetches.SelectMany(fetch => fetch.Sequences).ShouldAllBe(sequence => sequence <= uids.Length);
        entries.Select(entry => entry.Position).ShouldBe(uids[^19..].Select(uid => (long)uid));
    }

    [Fact]
    public async Task Entries_carry_the_internal_date_and_the_size_the_server_reported()
    {
        var mailbox = ScriptedMailbox.Of(
            new ScriptedMessage(11) { InternalDate = new DateTimeOffset(2026, 10, 19, 6, 30, 0, TimeSpan.Zero), Size = 51_234 });
        await using var harness = new MailboxHarness(mailbox);

        var entry = (await ListAsync(harness, cursor: 0)).ShouldHaveSingleItem();

        entry.Position.ShouldBe(11);
        entry.ReceivedAt.ShouldBe(new DateTimeOffset(2026, 10, 19, 6, 30, 0, TimeSpan.Zero));
        entry.Size.ShouldBe(51_234);
    }

    [Fact]
    public async Task The_epoch_is_the_uidvalidity_examine_reported()
    {
        await using var harness = new MailboxHarness(new ScriptedMailbox(uidValidity: 1_760_918_400, []));
        await using var session = await harness.OpenAsync();

        session.Epoch.ShouldBe(1_760_918_400);
    }

    public static TheoryData<string> BrokenProbeAnswers() => new()
    {
        "* 9 FETCH (FLAGS ())\r\n{0} OK FETCH completed\r\n",
        "* 2 FETCH (UID 4)\r\n{0} OK FETCH completed\r\n",
        "{0} OK FETCH completed\r\n",
        "{0} NO Scripted refusal\r\n",
        "{0} BAD Scripted protocol error\r\n",
    };

    [Theory]
    [MemberData(nameof(BrokenProbeAnswers))]
    public async Task A_probe_answer_without_its_uid_fails_closed_and_is_never_read_as_nothing_new_D5(string answer)
    {
        await using var harness = new MailboxHarness(ScriptedMailbox.WithUids([3, 6, 9, 12, 15, 18, 21, 24, 27]));
        harness.Server.Intercept = command => command == "FETCH 9 UID"
            ? new Interception.Raw(tag => string.Format(CultureInfo.InvariantCulture, answer, tag))
            : null;
        await using var session = await harness.OpenAsync();

        var listing = await session.ListAfterAsync(0, Cap, TestContext.Current.CancellationToken);

        listing.ShouldBe(new DmarcMailboxListing.Failed(DmarcSessionFault.Protocol));
    }

    [Fact]
    public async Task Uids_that_do_not_ascend_with_sequence_numbers_fail_closed_D5()
    {
        await using var harness = new MailboxHarness(ScriptedMailbox.WithUids([3, 6, 40, 12, 15, 18, 21, 24, 27]));
        await using var session = await harness.OpenAsync();

        var listing = await session.ListAfterAsync(10, Cap, TestContext.Current.CancellationToken);

        listing.ShouldBe(new DmarcMailboxListing.Failed(DmarcSessionFault.Protocol));
    }

    [Fact]
    public async Task A_window_answer_missing_a_message_fails_closed_D5()
    {
        await using var harness = new MailboxHarness(ScriptedMailbox.WithUids([3, 6, 9]));
        harness.Server.Intercept = command => command.StartsWith("FETCH 1:3", StringComparison.Ordinal)
            ? new Interception.Raw(tag =>
                "* 1 FETCH (UID 3 RFC822.SIZE 10 INTERNALDATE \"20-Oct-2026 08:00:00 +0000\")\r\n"
                + "* 3 FETCH (UID 9 RFC822.SIZE 10 INTERNALDATE \"20-Oct-2026 08:00:00 +0000\")\r\n"
                + $"{tag} OK FETCH completed\r\n")
            : null;
        await using var session = await harness.OpenAsync();

        var listing = await session.ListAfterAsync(0, Cap, TestContext.Current.CancellationToken);

        listing.ShouldBe(new DmarcMailboxListing.Failed(DmarcSessionFault.Protocol));
    }

    [Fact]
    public async Task After_a_failed_listing_the_session_sends_nothing_more()
    {
        await using var harness = new MailboxHarness(ScriptedMailbox.WithUids([3, 6, 9]));
        harness.Server.Intercept = command => command == "FETCH 3 UID" ? new Interception.TaggedBad() : null;
        await using var session = await harness.OpenAsync();
        await session.ListAfterAsync(0, Cap, TestContext.Current.CancellationToken);
        var sent = harness.Server.Commands.Count;

        var again = await session.ListAfterAsync(0, Cap, TestContext.Current.CancellationToken);
        var fetch = await session.FetchReportAsync(new DmarcMailboxEntry(3, MailboxHarness.Received, 10), TestContext.Current.CancellationToken);

        again.ShouldBe(new DmarcMailboxListing.Failed(DmarcSessionFault.Protocol));
        fetch.ShouldBe(new DmarcMailboxFetch.Failed(DmarcSessionFault.Protocol));
        harness.Server.Commands.Count.ShouldBe(sent);
    }

    private static async Task<IReadOnlyList<DmarcMailboxEntry>> ListAsync(MailboxHarness harness, long cursor)
    {
        await using var session = await harness.OpenAsync();
        var listing = await session.ListAfterAsync(cursor, Cap, TestContext.Current.CancellationToken);
        return listing.ShouldBeOfType<DmarcMailboxListing.Listed>().Entries;
    }

    /// <summary>Ascending UIDs with gaps of 1 to 9, the shape a mailbox has after deletions.</summary>
    private static uint[] GappedUids(Random random, int count)
    {
        var uids = new uint[count];
        uint uid = 0;
        for (var index = 0; index < count; index++)
        {
            uid += (uint)random.Next(1, 10);
            uids[index] = uid;
        }

        return uids;
    }

    /// <summary>Below every UID, above every UID, on a UID, in a gap, and at random.</summary>
    private static IEnumerable<long> Cursors(Random random, uint[] uids)
    {
        yield return 0;
        yield return uids[^1];
        yield return uids[^1] + 5L;
        yield return uids[random.Next(uids.Length)];
        yield return uids[random.Next(uids.Length)] - 1L;
        yield return random.Next(0, (int)uids[^1] + 2);
    }

    private static bool IsFetch(string command) =>
        command.StartsWith("FETCH ", StringComparison.Ordinal) || command.StartsWith("UID FETCH ", StringComparison.Ordinal);
}

/// <summary>A recorded non-UID FETCH, read back into its sequence numbers and its item list.</summary>
internal sealed record FetchCommand(IReadOnlyList<int> Sequences, string Items)
{
    public static FetchCommand Parse(string command)
    {
        command.ShouldStartWith("FETCH ");
        var parts = command["FETCH ".Length..].Split(' ', 2);
        var sequences = new List<int>();
        foreach (var range in parts[0].Split(','))
        {
            var bounds = range.Split(':').Select(bound => int.Parse(bound, CultureInfo.InvariantCulture)).ToArray();
            for (var sequence = bounds[0]; sequence <= bounds[^1]; sequence++)
                sequences.Add(sequence);
        }

        return new FetchCommand(sequences, parts[1].Trim('(', ')'));
    }
}
