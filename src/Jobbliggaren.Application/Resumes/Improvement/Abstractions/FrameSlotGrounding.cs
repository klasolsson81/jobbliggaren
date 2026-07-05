using System.Text.RegularExpressions;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Application.Resumes.Improvement.Abstractions;

/// <summary>
/// The ONE rule core for the three <c>FromFrame</c> invariants (Fas 4b PR-7, #656;
/// ADR 0093 §D2): (a) every noun-slot word is a word-boundary token of the cited Before
/// line (no synthesised nouns), (b) the resolved verb is a member of the strong-verb
/// closure at the catalog's pinned verb-mapping version (never a free verb), (c) every
/// number slot matches the Swedish decimal shape and rides verbatim ("aldrig påhittade
/// siffror", handoff §6.2). Surfaced as typed <see cref="DomainError"/> failures here
/// (expected client input errors → Result, CLAUDE.md §3); the
/// <see cref="ProposedChange.FromFrame"/> factory runs the SAME core and throws on
/// failure — defense-in-depth, one rule source (DRY).
/// <para>A <see cref="FrameSlotKind.Text"/> slot is a free user echo: neither grounded
/// nor format-constrained beyond non-whitespace. The personnummer defence for it lives
/// downstream (the apply guard / the preview redactor), deliberately not here — masking
/// at construction would hide the value from the boundary guard that must see it.</para>
/// </summary>
public static partial class FrameSlotGrounding
{
    // Invariant (c): an integer or Swedish/point decimal — 1-9 integer digits, optional
    // 1-4 decimals, no thousands separators (a counter, not a formatted figure). The value
    // is NEVER reparsed or reformatted downstream; it substitutes verbatim.
    [GeneratedRegex(@"^\d{1,9}([.,]\d{1,4})?$", RegexOptions.CultureInvariant)]
    private static partial Regex NumberShape();

    /// <summary>
    /// Validates <paramref name="slotInputs"/> against <paramref name="frame"/>'s slot
    /// contract, grounded in <paramref name="beforeLine"/> and endorsed by
    /// <paramref name="strongVerbSet"/>. Success means <see cref="ProposedChange.FromFrame"/>
    /// will not throw for the same inputs.
    /// </summary>
    public static Result Validate(
        CvFrame frame,
        IReadOnlyDictionary<string, string> slotInputs,
        string beforeLine,
        IReadOnlySet<string> strongVerbSet)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(slotInputs);
        ArgumentNullException.ThrowIfNull(strongVerbSet);

        // Arity first: exactly the frame's slot names, every value non-whitespace — a
        // template with a hole (or an unconsumed input) is never a mechanical substitution.
        if (slotInputs.Count != frame.Slots.Count
            || frame.Slots.Any(s => !slotInputs.ContainsKey(s.Name))
            || slotInputs.Values.Any(string.IsNullOrWhiteSpace))
        {
            return Result.Failure(DomainError.Validation(
                "Resume.FrameSlotArityMismatch",
                "Ram-inmatningen måste fylla exakt ramens fält, utan tomma värden."));
        }

        var beforeTokens = Tokenize(beforeLine);
        foreach (var slot in frame.Slots)
        {
            var value = slotInputs[slot.Name];
            switch (slot.Kind)
            {
                // (a) Every WORD of a noun slot must be a token of the cited line —
                // surface form, not lexemes: an inflected form the user did not write
                // would be a small synthesis (CTO D-B; OrdinalIgnoreCase is a case fold,
                // not a new word form — parity ReviewText.ContainsWord).
                case FrameSlotKind.Noun when Tokenize(value).Any(w => !beforeTokens.Contains(w)):
                    return Result.Failure(DomainError.Validation(
                        "Resume.FrameSlotNotGrounded",
                        "Ram-fältets ord måste finnas i den citerade raden."));

                // (c) A number slot is a bounded counter, echoed verbatim.
                case FrameSlotKind.Number when !NumberShape().IsMatch(value):
                    return Result.Failure(DomainError.Validation(
                        "Resume.FrameNumberInvalid",
                        "Antalet måste vara ett tal, med valfri decimal (komma eller punkt)."));

                // (b for the measure arm) The user's verb echo must be endorsed.
                case FrameSlotKind.Verb when !strongVerbSet.Contains(value.Trim()):
                    return Result.Failure(DomainError.Validation(
                        "Resume.FrameVerbNotStrong",
                        "Verbet måste väljas ur listan med starka handlingsverb."));
            }
        }

        // (b for the sentence arm) A fixed lead verb must resolve in the closure too —
        // the loader pins it at load time; re-verifying here keeps the invariant local
        // (defense-in-depth, CTO D-B).
        if (frame.Verb is { } fixedVerb && !strongVerbSet.Contains(fixedVerb.Trim()))
        {
            return Result.Failure(DomainError.Validation(
                "Resume.FrameVerbNotStrong",
                "Verbet måste väljas ur listan med starka handlingsverb."));
        }

        return Result.Success();
    }

    /// <summary>
    /// The resolved lead verb of a frame application: the frame's fixed verb (Sentence),
    /// else the user's single Verb-slot echo (Measure). Null only for a malformed
    /// frame/input pair (callers validate first).
    /// </summary>
    public static string? ResolveVerb(CvFrame frame, IReadOnlyDictionary<string, string> slotInputs)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(slotInputs);

        if (frame.Verb is { } fixedVerb)
        {
            return fixedVerb;
        }

        var verbSlot = frame.Slots.FirstOrDefault(s => s.Kind == FrameSlotKind.Verb);
        return verbSlot is not null && slotInputs.TryGetValue(verbSlot.Name, out var echo)
            ? echo.Trim()
            : null;
    }

    // Word-boundary tokens (Unicode-aware: åäö are letters, digits count so "3,5" splits
    // to its digit runs but a Number slot never reaches this path). Case-folded set for
    // OrdinalIgnoreCase membership — a case fold is not a new word form.
    private static HashSet<string> Tokenize(string text)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var isWord = i < text.Length && (char.IsLetter(text[i]) || char.IsDigit(text[i]));
            if (isWord && start < 0)
            {
                start = i;
            }
            else if (!isWord && start >= 0)
            {
                tokens.Add(text[start..i]);
                start = -1;
            }
        }

        return tokens;
    }
}
