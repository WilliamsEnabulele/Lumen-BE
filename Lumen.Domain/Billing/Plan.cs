namespace Lumen.Domain.Billing;

/// <summary>
/// What a student is buying.
///
/// The catalogue is a business decision nobody has made yet, so this is deliberately one plan
/// and a shape that takes more. What is not a placeholder is that the price lives here, in
/// code that is read and tested, rather than arriving from the client — a price a browser can
/// choose is a price a browser will choose.
/// </summary>
public sealed record Plan(string Code, string Name, Money Price, int GrantsDays)
{
    public static readonly Plan Monthly =
        new("monthly", "One month of tutoring", Money.FromNaira(2500), 30);

    public static readonly IReadOnlyList<Plan> All = [Monthly];

    public static Plan? Find(string? code) =>
        All.FirstOrDefault(plan => string.Equals(plan.Code, code, StringComparison.OrdinalIgnoreCase));
}
