using System.Text.Json.Nodes;

namespace Jobbliggaren.TestSupport;

/// <summary>
/// GitHub REST documents, in the shapes GitHub documents or is reported to produce (#1745). Each producer names what
/// makes the shape reachable. The field sets are the REST reference's at API version 2026-03-10 (read 2026-09-26):
/// <c>/user</c> carries a positive integer <c>id</c>, a changeable <c>login</c> and the PUBLIC profile address in
/// <c>email</c> (or null); every <c>/user/emails</c> entry carries <c>email</c>, <c>primary</c> and <c>verified</c> as
/// required JSON booleans and <c>visibility</c> as a string or null. Every test that needs a GitHub identity feeds one
/// of these to the PRODUCTION adapter; a shape GitHub does not document lives only in the adapter's own test, declared.
/// </summary>
internal static class GitHubApiShapes
{
    /// <summary>
    /// The authenticated user. <paramref name="publicEmail"/> is the profile's public address, which the user chooses
    /// and which need not be the primary one (the REST reference; ADR 0142 D8).
    /// </summary>
    public static string User(long id, string login, string? publicEmail = null) =>
        new JsonObject
        {
            ["login"] = login,
            ["id"] = id,
            ["node_id"] = "MDQ6VXNlcjE=",
            ["avatar_url"] = "https://avatars.githubusercontent.com/u/1?v=4",
            ["html_url"] = $"https://github.com/{login}",
            ["type"] = "User",
            ["site_admin"] = false,
            ["name"] = "Test Person",
            ["company"] = null,
            ["blog"] = "",
            ["location"] = null,
            ["email"] = publicEmail,
            ["hireable"] = null,
            ["bio"] = null,
            ["public_repos"] = 2,
            ["followers"] = 0,
            ["following"] = 0,
            ["created_at"] = "2020-01-01T00:00:00Z",
            ["updated_at"] = "2026-09-01T00:00:00Z",
        }.ToJsonString();

    /// <summary>The address GitHub hosts for a user who keeps the real one private; it receives no mail.</summary>
    public static string NoReplyAddress(long id, string login) => $"{id}+{login}@users.noreply.github.com";

    public static class Emails
    {
        /// <summary>One address, primary and verified: the documented response for an account with one address.</summary>
        public static string PrimaryVerified(string address) =>
            List(Entry(address, primary: true, verified: true, visibility: "private"));

        /// <summary>
        /// The primary address and, FIRST in the list, the noreply address GitHub hosts for the account. Reported in
        /// practice (Automattic/gravatar discussion #117, read 2026-09-26) as <c>primary:false, verified:true,
        /// visibility:null</c>; GitHub does not document the entry.
        /// </summary>
        public static string PrimaryVerifiedWithNoreply(string address, long id, string login) =>
            List(
                Entry(NoReplyAddress(id, login), primary: false, verified: true, visibility: null),
                Entry(address, primary: true, verified: true, visibility: "private"));

        /// <summary>
        /// The primary address and, FIRST in the list, an added address still waiting for its confirmation link:
        /// reachable, since a user can add an address at any time (the REST reference).
        /// </summary>
        public static string PrimaryVerifiedWithUnverifiedSecondary(string address, string other) =>
            List(
                Entry(other, primary: false, verified: false, visibility: null),
                Entry(address, primary: true, verified: true, visibility: "private"));

        private static JsonObject Entry(string address, bool primary, bool verified, string? visibility) =>
            new()
            {
                ["email"] = address,
                ["primary"] = primary,
                ["verified"] = verified,
                ["visibility"] = visibility,
            };

        private static string List(params JsonObject[] entries) => new JsonArray([.. entries]).ToJsonString();
    }
}
