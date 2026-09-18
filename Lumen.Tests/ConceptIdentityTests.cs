using Lumen.Domain.Courses;

namespace Lumen.Tests;

/// <summary>
/// Reprocessing an edited source document must not discard student progress. That reduces to
/// one property: a concept keeps its identity when its prose is rewritten, and loses it only
/// when it stops being the same concept.
/// </summary>
public class ConceptIdentityTests
{
    private static readonly Guid Course = Guid.Parse("0197b9c2-0000-7000-8000-000000000001");

    [Theory]
    [InlineData("Nested loops")]
    [InlineData("nested loops")]
    [InlineData("  Nested   Loops  ")]
    [InlineData("Nested loops!")]
    public void Case_punctuation_and_whitespace_are_not_identity(string title)
    {
        Assert.Equal(ConceptKey.From(Course, "Nested loops"), ConceptKey.From(Course, title));
    }

    [Fact]
    public void A_different_concept_gets_a_different_key()
    {
        Assert.NotEqual(ConceptKey.From(Course, "Nested loops"), ConceptKey.From(Course, "Single loops"));
    }

    [Fact]
    public void The_same_title_in_a_different_course_is_a_different_concept()
    {
        var otherCourse = Guid.Parse("0197b9c2-0000-7000-8000-000000000002");

        Assert.NotEqual(ConceptKey.From(Course, "Nested loops"), ConceptKey.From(otherCourse, "Nested loops"));
    }

    [Fact]
    public void A_concept_without_a_title_has_no_identity_to_derive()
    {
        Assert.Throws<ArgumentException>(() => ConceptKey.From(Course, "   "));
    }

    [Fact]
    public void Rewriting_the_explanation_keeps_the_concept_but_changes_its_fingerprint()
    {
        var before = new Concept
        {
            CourseId = Course,
            Title = "Nested loops",
            Body = "A loop inside the body of another loop.",
            Key = ConceptKey.From(Course, "Nested loops"),
            Fingerprint = ContentFingerprint.Of("A loop inside the body of another loop.")
        };

        const string rewritten = "A loop placed inside another loop's body, so the inner one restarts each turn.";
        var keyAfter = ConceptKey.From(Course, before.Title);
        var fingerprintAfter = ContentFingerprint.Of(rewritten);

        Assert.Equal(before.Key, keyAfter);                    // mastery records survive
        Assert.NotEqual(before.Fingerprint, fingerprintAfter);  // but the script gets rebuilt
    }

    [Fact]
    public void Reformatting_the_body_is_not_a_content_change()
    {
        Assert.Equal(
            ContentFingerprint.Of("A loop inside another loop."),
            ContentFingerprint.Of("A  loop   inside another loop."));
    }
}
