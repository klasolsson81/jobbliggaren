using System.Text;
using Jobbliggaren.Infrastructure.Mailbox.Dmarc;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Mailbox.Dmarc;

/// <summary>
/// #1981 PR 4a (architect V1) — the header unfolding the adapter does itself, table-tested at and just over the 8 KiB
/// and 4 KiB caps. It is one loop over lines; a block is read only when its blank line is among the bytes.
/// </summary>
public class MimeHeaderBlockTests
{
    [Fact]
    public void A_folded_field_is_unfolded_by_removing_each_line_break_before_whitespace()
    {
        var read = Read("Subject: x\r\nContent-Type: application/zip;\r\n\tname=\"r.zip\"\r\n  ; x=1\r\n\r\nbody");

        Text(read.Fields.ContentType).ShouldBe(" application/zip;\tname=\"r.zip\"  ; x=1");
    }

    [Fact]
    public void Field_names_match_without_regard_to_case_and_with_trailing_whitespace()
    {
        var read = Read("content-TYPE : text/xml\r\nCONTENT-TRANSFER-ENCODING:base64\r\ncontent-disposition: attachment\r\n\r\n");

        Text(read.Fields.ContentType).ShouldBe(" text/xml");
        Text(read.Fields.ContentTransferEncoding).ShouldBe("base64");
        Text(read.Fields.ContentDisposition).ShouldBe(" attachment");
    }

    [Fact]
    public void Bare_line_feeds_end_lines_as_well()
    {
        var read = Read("Content-Type: text/xml\n\n");

        Text(read.Fields.ContentType).ShouldBe(" text/xml");
    }

    [Fact]
    public void A_block_with_none_of_the_fields_reads_as_absent()
    {
        var read = Read("From: a@example.org\r\n\r\n");

        read.Fields.ShouldBe(new MimeHeaderFields(null, null, null));
    }

    [Fact]
    public void An_empty_block_is_complete()
    {
        MimeHeaderBlock.Read("\r\n"u8).ShouldBeOfType<MimeHeaderRead.Complete>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("Content-Type: text/xml")]
    [InlineData("Content-Type: text/xml\r\n")]
    [InlineData("Content-Type: text/xml\r\n continued")]
    public void A_block_without_its_blank_line_is_unterminated(string block)
    {
        MimeHeaderBlock.Read(Encoding.ASCII.GetBytes(block)).ShouldBeOfType<MimeHeaderRead.Unterminated>();
    }

    [Theory]
    [InlineData("Content-Type: text/plain\r\nContent-Type: application/zip\r\n\r\n")]
    [InlineData("Content-Disposition: inline\r\nX: y\r\ncontent-disposition: attachment\r\n\r\n")]
    [InlineData("Content-Transfer-Encoding: 7bit\r\nContent-Transfer-Encoding: base64\r\n\r\n")]
    public void A_field_that_appears_twice_is_ambiguous(string block)
    {
        MimeHeaderBlock.Read(Encoding.ASCII.GetBytes(block)).ShouldBeOfType<MimeHeaderRead.Ambiguous>();
    }

    [Fact]
    public void A_continuation_before_any_field_and_a_line_without_a_colon_are_ignored()
    {
        var read = Read(" stray continuation\r\nnot a field\r\nContent-Type: text/xml\r\n\r\n");

        Text(read.Fields.ContentType).ShouldBe(" text/xml");
    }

    [Theory]
    [InlineData(8192)]
    [InlineData(4096)]
    public void A_block_that_ends_exactly_at_a_cap_is_complete_and_one_byte_more_is_not(int cap)
    {
        var block = Padded(cap + 1);

        MimeHeaderBlock.Read(block.AsSpan(0, cap)).ShouldBeOfType<MimeHeaderRead.Unterminated>();
        MimeHeaderBlock.Read(Padded(cap)).ShouldBeOfType<MimeHeaderRead.Complete>();
    }

    [Fact]
    public void Only_the_header_is_read_when_a_body_follows_the_blank_line()
    {
        var read = Read("Content-Type: text/xml\r\n\r\nContent-Type: application/zip\r\n\r\n");

        Text(read.Fields.ContentType).ShouldBe(" text/xml");
    }

    private static MimeHeaderRead.Complete Read(string block) =>
        MimeHeaderBlock.Read(Encoding.ASCII.GetBytes(block)).ShouldBeOfType<MimeHeaderRead.Complete>();

    private static string? Text(byte[]? value) => value is null ? null : Encoding.ASCII.GetString(value);

    /// <summary>A block of exactly <paramref name="length"/> bytes whose blank line is its last two.</summary>
    private static byte[] Padded(int length)
    {
        var head = "Content-Type: text/xml\r\nX-Padding: ";
        return Encoding.ASCII.GetBytes(head + new string('x', length - head.Length - 4) + "\r\n\r\n");
    }
}
