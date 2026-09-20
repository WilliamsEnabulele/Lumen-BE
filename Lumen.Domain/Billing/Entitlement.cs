using Lumen.Domain.Common;

namespace Lumen.Domain.Billing;

/// <summary>
/// What a student has paid for, and until when.
///
/// Separate from the payment that bought it, because they answer different questions and have
/// different lifetimes: a payment is a thing that happened once and never changes again, and
/// an entitlement is a thing that is true today and may not be tomorrow.
/// </summary>
public sealed class Entitlement : Entity
{
    public Guid StudentId { get; set; }

    public string PlanCode { get; set; } = string.Empty;

    public DateTimeOffset GrantedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>The payment that bought this, so a support question has one thread to pull.</summary>
    public string PaymentReference { get; set; } = string.Empty;

    public bool IsActiveAt(DateTimeOffset now) => now < ExpiresAt;

    /// <summary>
    /// Extends an existing entitlement rather than replacing it.
    ///
    /// Paying again while you still have time left adds to it. Overwriting would mean someone
    /// who renews early is charged for days they then lose, which is the kind of quiet theft
    /// nobody notices until they do.
    /// </summary>
    public static Entitlement Grant(
        Entitlement? existing, Guid studentId, Plan plan, string paymentReference, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var from = existing is not null && existing.IsActiveAt(now) ? existing.ExpiresAt : now;

        var granted = existing ?? new Entitlement { StudentId = studentId, GrantedAt = now };
        granted.PlanCode = plan.Code;
        granted.PaymentReference = paymentReference;
        granted.ExpiresAt = from.AddDays(plan.GrantsDays);
        granted.UpdatedAt = now;

        return granted;
    }
}
