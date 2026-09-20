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

    /// <summary>
    /// Every confirmation this payment has received, in order, including the ones that changed
    /// nothing.
    ///
    /// The ones that changed nothing are the point. A duplicate delivery, a message that
    /// arrived after the payment had already settled, one whose signature did not check out —
    /// those are exactly what somebody investigating a disputed payment needs to see, and a
    /// log that only records the decisive message cannot show that four arrived.
    /// </summary>
    public List<WebhookConfirmation> Confirmations { get; set; } = [];

    public Money Amount => new(AmountKobo);

    public bool IsSettled => Status != PaymentStatus.Pending;

    /// <summary>
    /// Applies a confirmation that arrived from the provider.
    ///
    /// The confirmation is recorded whatever it says and whatever state this is already in —
    /// that record is the audit trail and it is never conditional. What is conditional is
    /// whether it moves anything: only the first confirmation of a payment still pending can,
    /// which is what makes a duplicate delivery harmless.
    ///
    /// Returns true when this call is the one that settled it, so the caller grants access
    /// exactly once no matter how many times Monnify reports the same payment.
    /// </summary>
    public bool Confirm(ConfirmedPayment confirmed, WebhookConfirmation received, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(confirmed);
        ArgumentNullException.ThrowIfNull(received);

        Confirmations.Add(received);
        UpdatedAt = now;

        if (IsSettled || !received.SignatureValid) return false;

        ProviderReference ??= confirmed.ProviderReference;

        if (!confirmed.SaysPaid)
        {
            // Not settled. A message that does not say money arrived is not a decision that it
            // never will — the next one may say it did, and closing the payment here would
            // strand a student who is midway through paying.
            return false;
        }

        var paid = confirmed.Paid!.Value;
        PaidKobo = paid.Kobo;
        SettledAt = now;

        if (confirmed.Currency is { Length: > 0 } currency
            && !string.Equals(currency, Currency, StringComparison.OrdinalIgnoreCase))
        {
            Status = PaymentStatus.Failed;
            Outcome = $"Paid in {currency}, but this costs {Currency}.";
            return true;
        }

        if (!paid.Covers(Amount))
        {
            Status = PaymentStatus.Underpaid;
            Outcome = $"{paid} arrived against a price of {Amount}.";
            return true;
        }

        Status = PaymentStatus.Paid;
        Outcome = $"Paid {paid}.";
        return true;
    }
}
