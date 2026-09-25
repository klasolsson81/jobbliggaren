using System.Text.Json.Nodes;

namespace Jobbliggaren.TestSupport;

/// <summary>
/// Google userinfo documents, in the shapes Google documents them (#1744). Each producer names what makes the shape
/// reachable. The claim set is Google's OpenID Connect reference (developers.google.com/identity/openid-connect/reference,
/// last updated 2026-03-27, read 2026-09-25): <c>sub</c>, <c>email</c>, <c>email_verified</c> as a JSON boolean, and
/// <c>hd</c> only for a Google Workspace or Cloud organisation account. Every test that needs an identity feeds one of
/// these to the PRODUCTION adapter; a shape Google does not document lives only in the adapter's own test, declared.
/// </summary>
internal static class GoogleUserInfoShapes
{
    /// <summary>A consumer Gmail account: Google hosts the mailbox, so it is authoritative for the address.</summary>
    public static string Gmail(string sub, string localPart) =>
        Document(sub, $"{localPart}@gmail.com", verified: true, hostedDomain: null);

    /// <summary>A Workspace account: <c>hd</c> names the organisation, whose administrator controls the mailbox.</summary>
    public static string Workspace(string sub, string address, string hostedDomain) =>
        Document(sub, address, verified: true, hostedDomain);

    /// <summary>
    /// A consumer Google account registered on a third-party address: Google verified the address once, and
    /// ownership may have changed since, so Google is not authoritative for it ("Verify the Google ID token", read
    /// 2026-09-25). Reachable: anyone can open a Google account on an address they hold.
    /// </summary>
    public static string ThirdPartyVerified(string sub, string address) =>
        Document(sub, address, verified: true, hostedDomain: null);

    /// <summary>A consumer account whose address Google has not verified.</summary>
    public static string Unverified(string sub, string address) =>
        Document(sub, address, verified: false, hostedDomain: null);

    private static string Document(string sub, string address, bool verified, string? hostedDomain)
    {
        var document = new JsonObject
        {
            ["sub"] = sub,
            ["name"] = "Test Person",
            ["given_name"] = "Test",
            ["family_name"] = "Person",
            ["picture"] = "https://lh3.googleusercontent.com/a/scripted",
            ["email"] = address,
            ["email_verified"] = verified,
        };
        if (hostedDomain is not null)
            document["hd"] = hostedDomain;

        return document.ToJsonString();
    }
}
