using Lumen.Domain.Billing;

namespace Lumen.Infrastructure.Billing;

/// <summary>Thrown when payments are asked for on a server that has no provider configured.</summary>
public sealed class PaymentsNotConfiguredException()
    : Exception("Payments are not configured on this server.");

/// <summary>
/// The provider on a server with no payment credentials.
///
/// It refuses rather than pretending to succeed, and it exists at all so that "payments are
/// switched off" is a clear answer from a live endpoint rather than a container that will not
/// start or a null somewhere in a request.
/// </summary>
public sealed class PaymentsUnavailable : IPaymentProvider
{
    public string Name => "none";

    public Task<StartedPayment> StartAsync(
        PaymentIntent intent, string customerName, string customerEmail, CancellationToken cancellationToken = default) =>
        throw new PaymentsNotConfiguredException();

    public Task<VerifiedPayment> VerifyAsync(
        string providerReference, CancellationToken cancellationToken = default) =>
        throw new PaymentsNotConfiguredException();
}

/// <summary>Whether a student has to have paid to be taught.</summary>
public sealed class BillingOptions
{
    public const string Section = "Billing";

    /// <summary>
    /// Null means "whatever the payment configuration implies" — a server with credentials
    /// charges, one without does not. Set it explicitly to charge or not charge regardless,
    /// which is what a staging environment with real credentials wants.
    /// </summary>
    public bool? Enforce { get; set; }
}

/// <summary>
/// Whether this server charges, resolved once at startup.
///
/// A record rather than a bare bool in the container, so what is being injected is legible at
/// the point of use: a handler asking for a <c>bool</c> says nothing about which one.
/// </summary>
public sealed record BillingEnforcement(bool Enforced);
