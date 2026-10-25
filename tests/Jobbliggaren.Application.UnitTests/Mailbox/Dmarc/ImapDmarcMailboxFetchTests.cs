using System.Text;
using Jobbliggaren.Application.Admin.Email.Dmarc;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Mailbox.Dmarc;

/// <summary>
/// #1981 PR 4a — choosing the report part from bounded header fetches (CTO r1 mechanism steps 3–5; security-auditor
/// Major 3 with her conditions). Every case is a scripted server's answer to HEADER, n.MIME and part fetches; no
/// BODYSTRUCTURE exists to script, because the adapter never asks for one.
/// </summary>
public class ImapDmarcMailboxFetchTests
{
    private static readonly byte[] Report = Encoding.ASCII.GetBytes("<?xml version=\"1.0\"?><feedback/>");

    public static TheoryData<string, DmarcReportPartKind> SinglePartTypes() => new()
    {
        { "application/gzip", DmarcReportPartKind.Gzip },
        { "application/x-gzip", DmarcReportPartKind.Gzip },
        { "application/zip", DmarcReportPartKind.Zip },
        { "application/x-zip-compressed", DmarcReportPartKind.Zip },
        { "text/xml", DmarcReportPartKind.Xml },
        { "application/xml; charset=UTF-8", DmarcReportPartKind.Xml },
        { "application/octet-stream; name=\"example.org!jobbliggaren.se!1!2.xml.gz\"", DmarcReportPartKind.Gzip },
        { "application/octet-stream; name=\"example.org!jobbliggaren.se!1!2.ZIP\"", DmarcReportPartKind.Zip },
        { "application/octet-stream; name=\"example.org!jobbliggaren.se!1!2.xml\"", DmarcReportPartKind.Xml },
    };

    [Theory]
    [MemberData(nameof(SinglePartTypes))]
    public async Task A_single_part_report_is_part_one_packed_as_its_media_type_or_name_says(string contentType, DmarcReportPartKind kind)
    {
        await using var harness = new MailboxHarness(ScriptedMailbox.Of(ScriptedMessage.SinglePart(5, Messages.TopHeader(contentType), Report)));

        var fetch = await harness.FetchOnlyAsync(5);

        var fetched = fetch.ShouldBeOfType<DmarcMailboxFetch.Fetched>();
        fetched.Kind.ShouldBe(kind);
        fetched.Content.ShouldBe(Report);
        harness.CommandsAfterExamine().ShouldBe(["UID FETCH 5 (BODY.PEEK[HEADER]<0.8192>)", "UID FETCH 5 (BODY.PEEK[1]<0.1048577>)"]);
    }

    [Fact]
    public async Task A_disposition_file_name_decides_where_the_media_type_does_not()
    {
        var header = Messages.TopHeader("application/octet-stream", "Content-Disposition: attachment;\r\n filename=\"r.xml.gz\"\r\n");
        await using var harness = new MailboxHarness(ScriptedMailbox.Of(ScriptedMessage.SinglePart(5, header, Report)));

        (await harness.FetchOnlyAsync(5)).ShouldBeOfType<DmarcMailboxFetch.Fetched>().Kind.ShouldBe(DmarcReportPartKind.Gzip);
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("message/rfc822; name=\"r.xml\"")]
    [InlineData("application/pdf; name=\"r.pdf\"")]
    [InlineData("this is not a content type")]
    public async Task A_single_part_that_is_no_report_is_skipped_after_the_header_alone(string contentType)
    {
        await using var harness = new MailboxHarness(ScriptedMailbox.Of(ScriptedMessage.SinglePart(5, Messages.TopHeader(contentType), Report)));

        (await harness.FetchOnlyAsync(5)).ShouldBe(new DmarcMailboxFetch.Skipped(DmarcSkipReason.NoReportPart));
        harness.CommandsAfterExamine().ShouldBe(["UID FETCH 5 (BODY.PEEK[HEADER]<0.8192>)"]);
    }

    [Fact]
    public async Task A_forensic_report_is_skipped_on_its_top_level_header_and_nothing_more_is_fetched()
    {
        var message = ScriptedMessage.Multipart(
            5,
            Messages.TopHeader("multipart/report;\r\n\treport-type=\"feedback-report\"; boundary=\"b\""),
            (Messages.PartHeader("text/plain"), Messages.Bytes("A failure report.")),
            (Messages.PartHeader("message/feedback-report"), Messages.Bytes("Feedback-Type: auth-failure")),
            (Messages.PartHeader("application/xml"), Report));
        await using var harness = new MailboxHarness(ScriptedMailbox.Of(message));

        (await harness.FetchOnlyAsync(5)).ShouldBe(new DmarcMailboxFetch.Skipped(DmarcSkipReason.Forensic));
        harness.CommandsAfterExamine().ShouldBe(["UID FETCH 5 (BODY.PEEK[HEADER]<0.8192>)"]);
    }

    [Fact]
    public async Task A_google_shaped_message_yields_its_zip_body_base64_decoded()
    {
        var zip = Enumerable.Range(0, 3000).Select(value => (byte)value).ToArray();
        await using var harness = new MailboxHarness(ScriptedMailbox.Of(Messages.GoogleShaped(5, zip)));

        var fetched = (await harness.FetchOnlyAsync(5)).ShouldBeOfType<DmarcMailboxFetch.Fetched>();

        fetched.Kind.ShouldBe(DmarcReportPartKind.Zip);
        fetched.Content.ShouldBe(zip);
        harness.CommandsAfterExamine().ShouldBe(["UID FETCH 5 (BODY.PEEK[HEADER]<0.8192>)", "UID FETCH 5 (BODY.PEEK[1]<0.1048577>)"]);
    }

    [Fact]
    public async Task A_yahoo_shaped_message_yields_its_zip_attachment_base64_decoded()
    {
        var zip = Enumerable.Range(0, 3000).Select(value => (byte)value).ToArray();
        await using var harness = new MailboxHarness(ScriptedMailbox.Of(Messages.YahooShaped(5, zip)));

        var fetched = (await harness.FetchOnlyAsync(5)).ShouldBeOfType<DmarcMailboxFetch.Fetched>();

        fetched.Kind.ShouldBe(DmarcReportPartKind.Zip);
        fetched.Content.ShouldBe(zip);
        harness.CommandsAfterExamine().ShouldBe(
        [
            "UID FETCH 5 (BODY.PEEK[HEADER]<0.8192>)",
            "UID FETCH 5 (BODY.PEEK[1.MIME]<0.4096>)",
            "UID FETCH 5 (BODY.PEEK[2.MIME]<0.4096>)",
            "UID FETCH 5 (BODY.PEEK[3.MIME]<0.4096>)",
            "UID FETCH 5 (BODY.PEEK[2]<0.1048577>)",
        ]);
    }

    [Fact]
    public async Task Base64_is_decoded_whatever_its_line_length()
    {
        var gzip = Enumerable.Range(0, 5000).Select(value => (byte)(value * 7)).ToArray();
        var oneLine = Messages.Bytes(Convert.ToBase64String(gzip));
        var message = ScriptedMessage.SinglePart(5, Messages.TopHeader("application/gzip", "Content-Transfer-Encoding: base64\r\n"), oneLine);
        await using var harness = new MailboxHarness(ScriptedMailbox.Of(message));

        (await harness.FetchOnlyAsync(5)).ShouldBeOfType<DmarcMailboxFetch.Fetched>().Content.ShouldBe(gzip);
    }

    [Fact]
    public async Task Quoted_printable_is_decoded()
    {
        var message = ScriptedMessage.SinglePart(
            5,
            Messages.TopHeader("text/xml", "Content-Transfer-Encoding: quoted-printable\r\n"),
            Messages.Bytes("<?xml version=3D\"1.0\"?><feed=\r\nback/>"));
        await using var harness = new MailboxHarness(ScriptedMailbox.Of(message));

        (await harness.FetchOnlyAsync(5)).ShouldBeOfType<DmarcMailboxFetch.Fetched>().Content.ShouldBe(Report);
    }

    [Theory]
    [InlineData("7bit")]
    [InlineData("8bit")]
    [InlineData("BINARY")]
    public async Task Identity_encodings_pass_the_bytes_through(string encoding)
    {
        var message = ScriptedMessage.SinglePart(5, Messages.TopHeader("text/xml", $"Content-Transfer-Encoding: {encoding}\r\n"), Report);
        await using var harness = new MailboxHarness(ScriptedMailbox.Of(message));

        (await harness.FetchOnlyAsync(5)).ShouldBeOfType<DmarcMailboxFetch.Fetched>().Content.ShouldBe(Report);
    }

    [Fact]
    public async Task An_encoding_the_adapter_does_not_decode_is_skipped_before_the_part_is_fetched()
    {
        var message = ScriptedMessage.SinglePart(5, Messages.TopHeader("application/gzip", "Content-Transfer-Encoding: x-uuencode\r\n"), Report);
        await using var harness = new MailboxHarness(ScriptedMailbox.Of(message));

        (await harness.FetchOnlyAsync(5)).ShouldBe(new DmarcMailboxFetch.Skipped(DmarcSkipReason.UnsupportedEncoding));
        harness.CommandsAfterExamine().ShouldBe(["UID FETCH 5 (BODY.PEEK[HEADER]<0.8192>)"]);
    }

    [Fact]
    public async Task A_top_level_header_that_ends_exactly_at_the_cap_is_read()
    {
        var header = PaddedHeader("application/gzip", DmarcIngestionLimits.HeaderFetchBytes);
        await using var harness = new MailboxHarness(ScriptedMailbox.Of(ScriptedMessage.SinglePart(5, header, Report)));

        (await harness.FetchOnlyAsync(5)).ShouldBeOfType<DmarcMailboxFetch.Fetched>();
    }

    [Fact]
    public async Task A_top_level_header_one_byte_over_the_cap_is_never_parsed_truncated_or_refetched()
    {
        var header = PaddedHeader("application/gzip", DmarcIngestionLimits.HeaderFetchBytes + 1);
        await using var harness = new MailboxHarness(ScriptedMailbox.Of(ScriptedMessage.SinglePart(5, header, Report)));

        (await harness.FetchOnlyAsync(5)).ShouldBe(new DmarcMailboxFetch.Skipped(DmarcSkipReason.HeaderTooLarge));
        harness.CommandsAfterExamine().ShouldBe(["UID FETCH 5 (BODY.PEEK[HEADER]<0.8192>)"]);
    }

    [Fact]
    public async Task A_part_header_over_its_cap_skips_the_message()
    {
        var message = ScriptedMessage.Multipart(
            5,
            Messages.TopHeader("multipart/mixed; boundary=\"b\""),
            (Messages.PartHeader("text/plain"), Messages.Bytes("text")),
            (PaddedHeader("application/zip", DmarcIngestionLimits.PartHeaderFetchBytes + 1), Report));
        await using var harness = new MailboxHarness(ScriptedMailbox.Of(message));

        (await harness.FetchOnlyAsync(5)).ShouldBe(new DmarcMailboxFetch.Skipped(DmarcSkipReason.HeaderTooLarge));
    }

    [Fact]
    public async Task A_part_of_exactly_the_cap_is_fetched_and_one_byte_more_is_too_large()
    {
        var atCap = new byte[DmarcIngestionLimits.MaxReportPartBytes];
        var overCap = new byte[DmarcIngestionLimits.MaxReportPartBytes + 1];
        await using var harness = new MailboxHarness(ScriptedMailbox.Of(
            ScriptedMessage.SinglePart(5, Messages.TopHeader("application/gzip"), atCap),
            ScriptedMessage.SinglePart(6, Messages.TopHeader("application/gzip"), overCap)));
        await using var session = await harness.OpenAsync();

        var fetched = await session.FetchReportAsync(Entry(5), TestContext.Current.CancellationToken);
        var skipped = await session.FetchReportAsync(Entry(6), TestContext.Current.CancellationToken);

        fetched.ShouldBeOfType<DmarcMailboxFetch.Fetched>().Content.Length.ShouldBe(DmarcIngestionLimits.MaxReportPartBytes);
        skipped.ShouldBe(new DmarcMailboxFetch.Skipped(DmarcSkipReason.TooLarge));
    }

    [Fact]
    public async Task Two_candidate_parts_read_neither()
    {
        var message = ScriptedMessage.Multipart(
            5,
            Messages.TopHeader("multipart/mixed; boundary=\"b\""),
            (Messages.PartHeader("application/gzip"), Report),
            (Messages.PartHeader("application/zip"), Report));
        await using var harness = new MailboxHarness(ScriptedMailbox.Of(message));

        (await harness.FetchOnlyAsync(5)).ShouldBe(new DmarcMailboxFetch.Skipped(DmarcSkipReason.MultipleReportParts));
        harness.CommandsAfterExamine().ShouldNotContain(command => command.Contains("BODY.PEEK[1]", StringComparison.Ordinal)
            || command.Contains("BODY.PEEK[2]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Nested_multiparts_and_embedded_messages_are_never_candidates_nor_descended_into()
    {
        var message = ScriptedMessage.Multipart(
            5,
            Messages.TopHeader("multipart/mixed; boundary=\"b\""),
            (Messages.PartHeader("multipart/mixed; boundary=\"c\"; name=\"r.zip\""), Report),
            (Messages.PartHeader("message/rfc822", disposition: "attachment; filename=\"r.xml\""), Report));
        await using var harness = new MailboxHarness(ScriptedMailbox.Of(message));

        (await harness.FetchOnlyAsync(5)).ShouldBe(new DmarcMailboxFetch.Skipped(DmarcSkipReason.NoReportPart));
        harness.CommandsAfterExamine().ShouldNotContain(command => command.Contains("[1.1", StringComparison.Ordinal)
            || command.Contains("[2.1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Inside_a_digest_a_part_without_a_content_type_is_an_embedded_message()
    {
        var message = ScriptedMessage.Multipart(
            5,
            Messages.TopHeader("multipart/digest; boundary=\"b\""),
            ("Content-Disposition: attachment; filename=\"r.xml\"\r\n\r\n", Report));
        await using var harness = new MailboxHarness(ScriptedMailbox.Of(message));

        (await harness.FetchOnlyAsync(5)).ShouldBe(new DmarcMailboxFetch.Skipped(DmarcSkipReason.NoReportPart));
    }

    [Fact]
    public async Task Only_the_first_four_parts_are_probed()
    {
        var text = (Messages.PartHeader("text/plain"), Messages.Bytes("text"));
        var message = ScriptedMessage.Multipart(
            5, Messages.TopHeader("multipart/mixed; boundary=\"b\""), text, text, text, text, (Messages.PartHeader("application/zip"), Report));
        await using var harness = new MailboxHarness(ScriptedMailbox.Of(message));

        (await harness.FetchOnlyAsync(5)).ShouldBe(new DmarcMailboxFetch.Skipped(DmarcSkipReason.NoReportPart));
        harness.CommandsAfterExamine().ShouldNotContain(command => command.Contains("[5.MIME]", StringComparison.Ordinal));
    }

    public static TheoryData<string> AbsentPartAnswers() => ["nil", "empty", "no", "bad"];

    [Theory]
    [MemberData(nameof(AbsentPartAnswers))]
    public async Task After_part_one_answered_any_non_answer_for_a_later_part_ends_the_probe(string answer)
    {
        var message = ScriptedMessage.Multipart(
            5,
            Messages.TopHeader("multipart/mixed; boundary=\"b\""),
            (Messages.PartHeader("text/plain"), Messages.Bytes("text")),
            (Messages.PartHeader("application/zip"), Report));
        message.Sections["3.MIME"] = answer switch
        {
            "nil" => new SectionAnswer.Nil(),
            "empty" => new SectionAnswer.Empty(),
            "no" => new SectionAnswer.No(),
            _ => new SectionAnswer.Bad(),
        };
        await using var harness = new MailboxHarness(ScriptedMailbox.Of(message));
        await using var session = await harness.OpenAsync();

        var fetch = await session.FetchReportAsync(Entry(5), TestContext.Current.CancellationToken);
        var listing = await session.ListAfterAsync(0, 10, TestContext.Current.CancellationToken);

        fetch.ShouldBeOfType<DmarcMailboxFetch.Fetched>().Kind.ShouldBe(DmarcReportPartKind.Zip);
        listing.ShouldBeOfType<DmarcMailboxListing.Listed>("the session is still usable");
        harness.CommandsAfterExamine().ShouldNotContain(command => command.Contains("[4.MIME]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_no_to_the_header_is_a_refusal_and_the_session_stays_usable()
    {
        var message = ScriptedMessage.SinglePart(5, Messages.TopHeader("application/gzip"), Report);
        message.Sections["HEADER"] = new SectionAnswer.No();
        await using var harness = new MailboxHarness(ScriptedMailbox.Of(message, ScriptedMessage.SinglePart(6, Messages.TopHeader("application/gzip"), Report)));
        await using var session = await harness.OpenAsync();

        (await session.FetchReportAsync(Entry(5), TestContext.Current.CancellationToken)).ShouldBe(new DmarcMailboxFetch.Refused());
        (await session.FetchReportAsync(Entry(6), TestContext.Current.CancellationToken)).ShouldBeOfType<DmarcMailboxFetch.Fetched>();
    }

    [Theory]
    [InlineData("1.MIME")]
    [InlineData("2")]
    public async Task A_no_to_part_one_or_to_the_part_itself_is_a_refusal(string section)
    {
        var message = Messages.YahooShaped(5, Report);
        message.Sections[section] = new SectionAnswer.No();
        await using var harness = new MailboxHarness(ScriptedMailbox.Of(message));

        (await harness.FetchOnlyAsync(5)).ShouldBe(new DmarcMailboxFetch.Refused());
    }

    [Theory]
    [InlineData("HEADER")]
    [InlineData("1.MIME")]
    [InlineData("2")]
    public async Task A_bad_to_the_header_part_one_or_the_part_ends_the_session(string section)
    {
        var message = Messages.YahooShaped(5, Report);
        message.Sections[section] = new SectionAnswer.Bad();
        await using var harness = new MailboxHarness(ScriptedMailbox.Of(message));
        await using var session = await harness.OpenAsync();

        var fetch = await session.FetchReportAsync(Entry(5), TestContext.Current.CancellationToken);
        var sent = harness.Server.Commands.Count;
        var after = await session.FetchReportAsync(Entry(5), TestContext.Current.CancellationToken);

        fetch.ShouldBe(new DmarcMailboxFetch.Failed(DmarcSessionFault.Protocol));
        after.ShouldBe(new DmarcMailboxFetch.Failed(DmarcSessionFault.Protocol));
        harness.Server.Commands.Count.ShouldBe(sent, "a BAD is never followed by the same command in the same run");
    }

    [Fact]
    public async Task A_message_gone_from_the_mailbox_is_skipped_as_not_in_the_mailbox()
    {
        await using var harness = new MailboxHarness(ScriptedMailbox.Of(ScriptedMessage.SinglePart(5, Messages.TopHeader("application/gzip"), Report)));

        (await harness.FetchOnlyAsync(99)).ShouldBe(new DmarcMailboxFetch.Skipped(DmarcSkipReason.NotInMailbox));
        harness.CommandsAfterExamine().ShouldBe(["UID FETCH 99 (BODY.PEEK[HEADER]<0.8192>)"]);
    }

    // RFC 3501 §6.4.8: a UID FETCH of a UID the mailbox no longer holds is answered OK with no untagged FETCH.
    [Theory]
    [InlineData("HEADER")]
    [InlineData("1.MIME")]
    [InlineData("2")]
    public async Task A_section_answered_ok_with_no_data_is_a_message_not_in_the_mailbox(string section)
    {
        await using var harness = new MailboxHarness(ScriptedMailbox.Of(Messages.YahooShaped(5, Report)));
        await using var session = await harness.OpenAsync();
        harness.Server.Intercept = command => command.Contains($"BODY.PEEK[{section}]<", StringComparison.Ordinal)
            ? new Interception.Raw(tag => $"{tag} OK FETCH completed\r\n")
            : null;

        var fetch = await session.FetchReportAsync(Entry(5), TestContext.Current.CancellationToken);
        var listing = await session.ListAfterAsync(0, 10, TestContext.Current.CancellationToken);

        fetch.ShouldBe(new DmarcMailboxFetch.Skipped(DmarcSkipReason.NotInMailbox));
        listing.ShouldBeOfType<DmarcMailboxListing.Listed>("the session is still usable");
        harness.CommandsAfterExamine().Last(command => command.StartsWith("UID FETCH", StringComparison.Ordinal))
            .ShouldContain($"BODY.PEEK[{section}]<", Case.Sensitive, "nothing more of the message is fetched");
    }

    [Fact]
    public async Task A_part_header_with_a_field_twice_reads_no_part()
    {
        var message = ScriptedMessage.Multipart(
            5,
            Messages.TopHeader("multipart/mixed; boundary=\"b\""),
            ("Content-Type: text/plain\r\nContent-Type: application/zip\r\n\r\n", Report));
        await using var harness = new MailboxHarness(ScriptedMailbox.Of(message));

        (await harness.FetchOnlyAsync(5)).ShouldBe(new DmarcMailboxFetch.Skipped(DmarcSkipReason.NoReportPart));
    }

    [Fact]
    public async Task No_log_line_carries_the_password_a_header_value_or_a_file_name()
    {
        var refused = Messages.YahooShaped(6, Report);
        refused.Sections["2"] = new SectionAnswer.No();
        var broken = Messages.YahooShaped(7, Report);
        broken.Sections["2"] = new SectionAnswer.Bad();
        await using var harness = new MailboxHarness(ScriptedMailbox.Of(Messages.YahooShaped(5, Report), refused, broken));
        await using (var session = await harness.OpenAsync())
        {
            foreach (var uid in new uint[] { 5, 6, 7 })
                await session.FetchReportAsync(Entry(uid), TestContext.Current.CancellationToken);
        }

        harness.Logger.Records.ShouldNotBeEmpty();
        harness.Logger.Records.ShouldAllBe(record => record.EventId.Id >= 3135 && record.EventId.Id <= 3139);
        foreach (var record in harness.Logger.Records)
        {
            var text = record.Message + string.Join(' ', record.Properties.Select(property => property.Value));
            text.ShouldNotContain(harness.Password);
            text.ShouldNotContain(Messages.Canary);
            text.ShouldNotContain("example.org", Case.Insensitive);
            text.ShouldNotContain("Scripted", Case.Insensitive);
        }
    }

    private static DmarcMailboxEntry Entry(uint uid) => new(uid, MailboxHarness.Received, 4096);

    /// <summary>A header block of exactly <paramref name="length"/> bytes, its blank line last.</summary>
    private static string PaddedHeader(string contentType, int length)
    {
        var head = $"Content-Type: {contentType}\r\nX-Padding: ";
        var padding = length - head.Length - "\r\n\r\n".Length;
        return head + new string('x', padding) + "\r\n\r\n";
    }
}
