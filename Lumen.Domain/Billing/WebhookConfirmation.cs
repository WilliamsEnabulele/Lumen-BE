namespace Lumen.Domain.Billing;

/// <summary>
/// A confirmation that arrived from the payment provider, kept exactly as it arrived.
///
/// This is the evidence. Confirmation comes from the webhook and nowhere else, so the message
/// that moved a payment is the only record of why it moved — and a payment that changed state
/// for reasons nobody can reconstruct is one nobody can answer a complaint about. The raw body
/// is kept verbatim rather than as parsed fields, because the parse is the part most likely to
/// be wrong, and a stored parse cannot be re-read once the bug is found.
/// </summary>
/// <param name="Signature">
/// The signature the message carried. Kept so a confirmation can be re-verified later against
/// the secret that was in force, which is the only way to answer "was this real" after the
/// fact.
/// </param>
public sealed record WebhookConfirmation(
    DateTimeOffset ReceivedAt,
    string RawBody,
    string? Signature,
    bool SignatureValid,
    string? EventType,
    string? ProviderStatus,
    long? PaidKobo,
    string? Currency,
    string Outcome);

/// <summary>
/// What a signed webhook body says about a payment.
///
/// Every field is nullable because this is parsed from somebody else's JSON, and the handling
/// of a field that is not there has to be a decision rather than an exception. The decision is
/// always the same: without an amount there is no confirmation, because "they paid" with no
/// number attached is not something to grant a subscription on.
/// </summary>
public sealed record ConfirmedPayment(
    string? OurReference,
    string? ProviderReference,
    string? EventType,
    string? Status,
    Money? Paid,
    string? Currency)
{
    /// <summary>
    /// Whether the message says, unambiguously, that money arrived.
    ///
    /// One status counts. Listing the failures instead would mean a status nobody anticipated
    /// arrives as a success, and the ways a payment can not-happen are added to more often
    /// than the ways it can.
    ///
    /// A stated status settles it, and the event type is consulted only when no status came at
    /// all. Accepting either would make a contradictory message — a success event carrying a
    /// pending status — read as paid off the half that happens to agree, and a contradiction
    /// is precisely where a confirmation has to fail closed rather than pick a side.
    /// </summary>
    public bool SaysPaid =>
        Paid is not null
        && (Status is { Length: > 0 }
            ? string.Equals(Status, "PAID", StringComparison.OrdinalIgnoreCase)
            : string.Equals(EventType, "SUCCESSFUL_TRANSACTION", StringComparison.OrdinalIgnoreCase));
}
