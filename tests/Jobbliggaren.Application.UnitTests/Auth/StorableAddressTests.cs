using Jobbliggaren.Infrastructure.Auth;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// Which addresses may become an account's stored address (security-auditor MA-1, 2026-09-21). A predicate over
/// character classes, never a list of letters: which letters an address may hold is the two email validators'
/// question, not this one's.
/// </summary>
public sealed class StorableAddressTests
{
    [Theory]
    [InlineData("sam@example.se")]
    [InlineData("björn@example.se")]
    [InlineData("o'brien@example.se")]
    [InlineData("a!b#c@example.se")]
    [InlineData("ſam@example.se")]
    public void An_address_of_visible_characters_is_storable(string address) =>
        StorableAddress.IsStorable(address).ShouldBeTrue();

    [Theory]
    [InlineData(" sam@example.se", "leading space")]
    [InlineData("sam@example.se ", "trailing space")]
    [InlineData("a b@example.se", "inner space")]
    [InlineData("a\u00A0b@example.se", "no-break space")]
    [InlineData("a\u0000b@example.se", "NUL")]
    [InlineData("a\r\nb@example.se", "CR LF")]
    [InlineData("sam@example.se\n", "trailing LF")]
    [InlineData("a\u202Eb@example.se", "right-to-left override (Cf)")]
    [InlineData("a\u200Bb@example.se", "zero width space (Cf)")]
    [InlineData("a\u3000b@example.se", "ideographic space (Zs)")]
    [InlineData("a\u2028b@example.se", "line separator (Zl)")]
    [InlineData("a\u00ADb@example.se", "soft hyphen (Cf)")]
    [InlineData("a\uFEFFb@example.se", "byte order mark (Cf)")]
    [InlineData("a\uD83D\uDE00b@example.se", "an astral character (a surrogate pair)")]
    public void An_address_carrying_a_control_whitespace_surrogate_or_format_character_is_not_storable(
        string address, string because) =>
        StorableAddress.IsStorable(address).ShouldBeFalse(because);
}
