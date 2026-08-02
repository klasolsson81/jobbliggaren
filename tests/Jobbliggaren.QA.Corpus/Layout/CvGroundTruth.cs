namespace Jobbliggaren.QA.Corpus.Layout;

/// <summary>
/// Validates that a <see cref="CvModel"/>'s markers can actually carry the oracle.
///
/// <para>The whole content-loss measurement is a substring trace: an employer string is looked for
/// in the extracted bytes, then inside the parsed artifact, then inside the promoted CV's
/// experience span. Every step of that is meaningless if a marker is ambiguous — if "Sigma IT"
/// were also a substring of a project line, a marker that was genuinely dropped would still be
/// FOUND and the row would report survival. So an ambiguous marker is a hard INSTRUMENT failure,
/// not a finding: the corpus refuses to report rather than reporting a number it cannot stand
/// behind.</para>
/// </summary>
public static class CvGroundTruth
{
    /// <summary>Returns one message per violation; empty means the model can carry the oracle.</summary>
    public static IReadOnlyList<string> Validate(CvModel m)
    {
        ArgumentNullException.ThrowIfNull(m);

        var problems = new List<string>();
        var markers = m.EmploymentMarkers.Concat(m.EducationMarkers).ToList();

        if (markers.Distinct(StringComparer.Ordinal).Count() != markers.Count)
            problems.Add("two markers are identical, so a trace cannot tell them apart");

        // No marker may be a substring of another: "Sigma" inside "Sigma IT" would make a lost
        // marker read as present.
        foreach (var a in markers)
        {
            foreach (var b in markers)
            {
                if (!ReferenceEquals(a, b) && a != b && b.Contains(a, StringComparison.Ordinal))
                    problems.Add($"marker '{a}' is a substring of marker '{b}'");
            }
        }

        // A marker inside a heading, a profile line or a project line would survive the trace
        // even when its own entry was dropped, because those texts land in other fields.
        var otherText = new List<string>(m.ProfileLines)
        {
            m.Headings.Profile, m.Headings.Experience, m.Headings.Education,
            m.Headings.Skills, m.Headings.Languages, m.Headings.KnownProjects,
            m.Headings.UnknownProjects, m.PersonName, m.Email, m.Phone, m.City,
        };
        otherText.AddRange(m.ProjectLines);
        otherText.AddRange(m.Skills);
        otherText.AddRange(m.Languages);
        otherText.AddRange(m.Employments.Select(e => e.Role));
        otherText.AddRange(m.Employments.Select(e => e.Bullet));
        otherText.AddRange(m.Employments.Select(e => e.Period));
        otherText.AddRange(m.Educations.Select(e => e.Degree));
        otherText.AddRange(m.Educations.Select(e => e.Period));

        // #1060 D3(β-3): an UnattributedBlock carries no marker of its own — it has no employer,
        // which is the whole point of it — but its text is rendered under the experience heading
        // and therefore lands in the same document every marker is traced through. It is folded
        // in here rather than exempted, because the failure it would otherwise enable is the one
        // this class exists to refuse: an employer that was genuinely dropped could be found
        // inside a freelance role line or bullet and read as present.
        otherText.AddRange(m.UnattributedExperience.Select(e => e.Role));
        otherText.AddRange(m.UnattributedExperience.Select(e => e.Bullet));
        otherText.AddRange(m.UnattributedExperience.Select(e => e.Period));

        foreach (var marker in markers)
        {
            foreach (var text in otherText)
            {
                if (text.Contains(marker, StringComparison.Ordinal))
                    problems.Add($"marker '{marker}' also occurs in non-entry text '{Trim(text)}'");
            }
        }

        // The count-only half of the oracle needs an asymmetric seed: with equal cardinalities a
        // bug that reads the education list while reporting experience scores green.
        if (m.GroundTruthEmployments == m.GroundTruthEducations)
        {
            problems.Add(
                $"employments and educations are both {m.GroundTruthEmployments}; the counts must "
                + "differ or a count-only reading cannot tell which side it measured");
        }

        return problems;
    }

    private static string Trim(string s) => s.Length <= 40 ? s : s[..40] + "…";
}
