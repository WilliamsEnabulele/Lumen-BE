using Lumen.Domain.Common;

namespace Lumen.Domain.Billing;

public enum PaymentStatus
{
    /// <summary>Created and sent to the provider. Nothing has been paid.</summary>
    Pending = 0,

    /// <summary>Paid in full and verified against the provider. The only state that grants anything.</summary>
    Paid = 1,

    /// <summary>The provider says it will not complete. Terminal.</summary>
    Failed = 2,

    /// <summary>
    /// Money arrived, but not enough of it. Terminal, and deliberately not Failed: somebody is
    /// owed either the rest of the service or a refund, and a state that says "failed" hides
    /// the fact that a real payment is sitting there.
    /// </summary>
    Underpaid = 3
}

/// <summary>
/// One attempt to pay for one plan.
///
/// The reference is ours and is generated before the provider is called, which is what makes
/// the whole flow idempotent: the provider can tell us about the same payment three times, the
/// student can refresh the callback page, and a retry can arrive after a timeout, and all of
/// them land on the same row.
/// </summary>
public sealed class PaymentIntent : Entity
{
    public Guid StudentId { get; set; }

    /// <summary>Our reference, unique, and the idempotency key for everything that follows.</summary>
    public string Reference { get; set; } = string.Empty;

    public string PlanCode { get; set; } = string.Empty;

    /// <summary>What was asked for, in kobo. Never re-read from the provider or the client.</summary>
    public long AmountKobo { get; set; }

    public string Currency { get; set; } = "NGN";

    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

    /// <summary>The provider's own reference, kept for support and for reconciliation.</summary>
    public string? ProviderReference { get; set; }

    /// <summary>What actually arrived, once anything has.</summary>
    public long? PaidKobo { get; set; }

    public DateTimeOffset? SettledAt { get; set; }

    /// <summary>Why it ended where it did, in words, for whoever has to answer for it later.</summary>
    public string? Outcome { get; set; }

    public Money Amount => new(AmountKobo);

    public bool IsSettled => Status != PaymentStatus.Pending;

    /// <summary>
    /// Applies a verified payment.
    ///
    /// Verified, emphatically: the argument is what the provider said when <em>we</em> asked it,
    /// never what arrived in a webhook. A webhook says something happened; it is not evidence
    /// of what happened, and treating it as evidence means anyone who can post to the endpoint
    /// can grant themselves a subscription.
    ///
    /// Returns true when this call is the one that moved it, so the caller can grant access
    /// exactly once no matter how many times the same payment is reported.
    /// </summary>
    public bool Settle(VerifiedPayment verified, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(verified);

        if (IsSettled) return false;

        ProviderReference = verified.ProviderReference;
        PaidKobo = verified.Paid.Kobo;
        SettledAt = now;
        UpdatedAt = now;

        if (!string.Equals(verified.Currency, Currency, StringComparison.OrdinalIgnoreCase))
        {
            Status = PaymentStatus.Failed;
            Outcome = $"Paid in {verified.Currency}, but this costs {Currency}.";
            return true;
        }

        if (!verified.Succeeded)
        {
            Status = PaymentStatus.Failed;
            Outcome = verified.ProviderStatus is { Length: > 0 } reported
                ? $"The payment did not complete: {reported}."
                : "The payment did not complete.";
            return true;
        }

        if (!verified.Paid.Covers(Amount))
        {
            Status = PaymentStatus.Underpaid;
            Outcome = $"{verified.Paid} arrived against a price of {Amount}.";
            return true;
        }

        Status = PaymentStatus.Paid;
        Outcome = $"Paid {verified.Paid}.";
        return true;
    }
}

/// <summary>
/// What the provider says about a payment when asked directly.
///
/// A separate type from the webhook payload on purpose. They carry similar fields and only one
/// of them is trustworthy, and giving them the same shape is how the untrusted one ends up
/// flowing into a decision.
/// </summary>
public sealed record VerifiedPayment(
    string ProviderReference,
    string OurReference,
    Money Paid,
    string Currency,
    bool Succeeded,
    string? ProviderStatus);
