using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Jobbliggaren.TestSupport;

/// <summary>
/// LinkedIn userinfo documents, in the shapes LinkedIn documents or that follow from its documentation (#1746). Each
/// producer names what makes the shape reachable. The field set is the "Sample API Response" of "Sign In with LinkedIn
/// using OpenID Connect" (updated 2024-08-08, read 2026-09-27): <c>sub</c>, <c>name</c>, <c>given_name</c>,
/// <c>family_name</c>, <c>picture</c>, <c>locale</c>, <c>email</c> and <c>email_verified</c> as a Boolean, where the
/// page says <i>"The 'email' and 'email_verified' fields are optional and may not be included in all responses."</i>
/// Every test that needs a LinkedIn identity feeds one of these to the PRODUCTION adapter; a shape LinkedIn does not
/// document lives only in the adapter's own test, declared.
/// </summary>
internal static class LinkedInUserInfoShapes
{
    /// <summary>The documented sample's subject: a pairwise identifier, case-sensitive.</summary>
    public const string DocumentedSub = "782bbtaQ";

    /// <summary>A fresh subject in the sample's character class; the real length is not measured.</summary>
    public static string NewSub() => RandomNumberGenerator.GetString("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789", 10);

    /// <summary>The documented sample: every field, the address verified.</summary>
    public static string Member(string sub, string email) => Document(sub, email, verified: true, withProfile: true);

    /// <summary>
    /// The fields scope <c>openid email</c> is documented to carry, without <c>profile</c>'s. DERIVED from the scope
    /// table, not measured: the first real login reads it.
    /// </summary>
    public static string EmailScopeOnly(string sub, string email) => Document(sub, email, verified: true, withProfile: false);

    /// <summary>A Boolean <c>false</c>: the documented type, a value not measured.</summary>
    public static string Unverified(string sub, string email) => Document(sub, email, verified: false, withProfile: true);

    /// <summary>Neither <c>email</c> nor <c>email_verified</c>: both are documented as optional.</summary>
    public static string WithoutAddress(string sub) => Document(sub, email: null, verified: null, withProfile: true);

    /// <summary>An address without its flag: each field is documented as optional on its own.</summary>
    public static string AddressWithoutFlag(string sub, string email) =>
        Document(sub, email, verified: null, withProfile: true);

    /// <summary>A flag without its address: each field is documented as optional on its own.</summary>
    public static string FlagWithoutAddress(string sub) => Document(sub, email: null, verified: true, withProfile: true);

    private static string Document(string sub, string? email, bool? verified, bool withProfile)
    {
        var document = new JsonObject { ["sub"] = sub };
        if (withProfile)
        {
            document["name"] = "Anna Berg";
            document["given_name"] = "Anna";
            document["family_name"] = "Berg";
            document["picture"] = "https://media.licdn.com/dms/image/scripted/profile-displayphoto-shrink_100_100/0/";
            document["locale"] = new JsonObject { ["country"] = "SE", ["language"] = "sv" };
        }

        if (email is not null)
            document["email"] = email;
        if (verified is { } flag)
            document["email_verified"] = flag;
        return document.ToJsonString();
    }
}
