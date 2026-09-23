namespace Jobbliggaren.Application.Common.Validation;

/// <summary>
/// The one length bound every validator puts on a submitted e-mail address. Identity's <c>EmailAddressAttribute</c>
/// and the <c>NormalizedEmail</c> column both stay well within it. It is also what the grant store's padding ceiling
/// is computed from (#1794): an address longer than this reaches no store, so the two cannot drift apart.
/// </summary>
public static class EmailAddressRules
{
    public const int MaximumLength = 256;
}
