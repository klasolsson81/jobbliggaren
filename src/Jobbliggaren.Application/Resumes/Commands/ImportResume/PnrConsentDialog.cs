namespace Jobbliggaren.Application.Resumes.Commands.ImportResume;

/// <summary>
/// The personnummer consent dialog's version identifier (CV-pivot 5b, CTO-bind M-C). Stamped
/// as <c>ResumeFile.PnrConsentDialogVersion</c> on every consented flagged-file capture — the
/// "informed" half of the Art. 7(1) evidence: which dialog copy the user actually acknowledged.
/// An Application CONSTANT, deliberately not knowledge-bank data: the version must move in
/// lockstep with the dialog copy it names (<c>messages/sv.json</c>, deployed with the app), and
/// a data file would decouple the two. Bump ONLY when the dialog copy's material terms change;
/// existing rows keep the value they were stamped with (write-once aggregate).
/// </summary>
public static class PnrConsentDialog
{
    public const string Version = "1";
}
