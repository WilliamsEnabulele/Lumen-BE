namespace Lumen.Domain.Billing;

/// <summary>
/// Who may turn a document into a course.
///
/// The meter is uploads, not lessons, and that choice is the product. Uploading is where the
/// expensive, one-off work happens — a whole document read by a model — and metering it means
/// a free student gets a real course rather than a trial that stops mid-explanation. Being
/// taught from something you were allowed to create is never charged for twice.
/// </summary>
public static class Access
{
    /// <summary>
    /// Documents a student may turn into courses each calendar month without paying.
    ///
    /// One, and one is deliberate rather than stingy: the thing being demonstrated is whether
    /// a whole document becomes a lesson worth sitting through, and that is answered by one
    /// document. A second adds nothing to the decision and doubles the cost of people who
    /// were never going to pay.
    /// </summary>
    public const int FreeUploadsPerMonth = 1;

    public static bool MayUpload(bool enforced, Entitlement? entitlement, int uploadsThisMonth, DateTimeOffset now) =>
        !enforced
        || (entitlement?.IsActiveAt(now) ?? false)
        || uploadsThisMonth < FreeUploadsPerMonth;

    /// <summary>
    /// How many free uploads are left this month. Negative is impossible; zero means the next
    /// one needs a subscription.
    /// </summary>
    public static int FreeUploadsLeft(int uploadsThisMonth) =>
        Math.Max(0, FreeUploadsPerMonth - uploadsThisMonth);

    /// <summary>
    /// When the free allowance comes back — the first instant of next month, in UTC.
    ///
    /// Calendar months rather than a rolling thirty days, because "you get one a month" is a
    /// promise people check against a calendar, and a rolling window makes somebody who
    /// uploaded on the 31st wait until the 30th of a month that has no 31st.
    /// </summary>
    public static DateTimeOffset AllowanceResetsAt(DateTimeOffset now) =>
        new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1);
}
