using Lumen.Domain.Billing;
using Lumen.Infrastructure.Billing;
using Lumen.Infrastructure.Storage;

namespace Lumen.Api.Endpoints;

public sealed record StartPaymentRequest(string PlanCode, string? CustomerName, string? CustomerEmail);

/// <summary>
/// Taking money, and deciding what it bought.
///
/// The shape of this is one rule repeated: <b>nothing that arrives from outside decides
/// anything.</b> Not the amount, which comes from the plan in code rather than from the client.
/// Not the webhook, which is a hint that something happened and is never itself the evidence.
/// Only an answer Monnify gave when we asked it directly moves a payment out of pending.
/// </summary>
public static class BillingEndpoints
{
    private static readonly Guid DefaultStudent = Guid.Parse("0197b9c2-0000-7000-8000-000000000002");

    public static void MapBillingEndpoints(this WebApplication app)
    {
        app.MapGet("/api/plans", () => Results.Ok(Plan.All.Select(plan => new
        {
            code = plan.Code,
            name = plan.Name,
            price = plan.Price.ToNairaString(),
            currency = "NGN",
            days = plan.GrantsDays,
        })));

        app.MapPost("/api/payments", async (
            StartPaymentRequest request,
            IPaymentProvider provider,
            IPaymentStore payments) =>
        {
            var plan = Plan.Find(request.PlanCode);
            if (plan is null) return Results.BadRequest(new { error = "There is no such plan." });

            var email = request.CustomerEmail?.Trim();
            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
                return Results.BadRequest(new { error = "An email address is needed to take a payment." });

            // The price is read from the plan, never from the request. A price the client can
            // name is a price the client will name, and it will be zero.
            var intent = new PaymentIntent
            {
                StudentId = DefaultStudent,
                Reference = PaymentReference.Next(),
                PlanCode = plan.Code,
                AmountKobo = plan.Price.Kobo,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            // Saved before the provider is called, so a timeout leaves a record we can verify
            // against rather than a payment nobody here has ever heard of.
            payments.Save(intent);

            try
            {
                var started = await provider.StartAsync(
                    intent, request.CustomerName?.Trim() ?? "Lumen student", email);

                intent.ProviderReference = started.ProviderReference;
                payments.Save(intent);

                return Results.Ok(new { reference = intent.Reference, checkoutUrl = started.CheckoutUrl });
            }
            catch (PaymentsNotConfiguredException)
            {
                payments.Save(intent);
                return Results.Json(
                    new { error = "Payments are not set up on this server." }, statusCode: 503);
            }
            catch (InvalidOperationException failure)
            {
                intent.Status = PaymentStatus.Failed;
                intent.Outcome = failure.Message;
                payments.Save(intent);

                return Results.BadRequest(new { error = "That payment could not be started. Nothing has been charged." });
            }
        });

        // Where the student lands after paying. Verifies rather than believing the redirect,
        // and means the flow completes even when the webhook never arrives — which it will not,
        // eventually, for somebody.
        app.MapGet("/api/payments/{reference}", async (
            string reference,
            IPaymentProvider provider,
            IPaymentStore payments,
            IEntitlementStore entitlements) =>
        {
            if (!PaymentReference.IsWellFormed(reference)) return Results.NotFound();

            var intent = payments.Find(reference);
            if (intent is null) return Results.NotFound();

            if (!intent.IsSettled && intent.ProviderReference is { Length: > 0 })
                await Settle(intent, provider, payments, entitlements);

            return Results.Ok(new
            {
                reference = intent.Reference,
                status = intent.Status.ToString(),
                plan = intent.PlanCode,
                amount = intent.Amount.ToNairaString(),
                paid = intent.PaidKobo is long kobo ? new Money(kobo).ToNairaString() : null,
                outcome = intent.Outcome,
            });
        });

        app.MapGet("/api/entitlement", (IEntitlementStore entitlements) =>
        {
            var granted = entitlements.For(DefaultStudent);
            var now = DateTimeOffset.UtcNow;

            return Results.Ok(new
            {
                active = granted?.IsActiveAt(now) ?? false,
                plan = granted?.PlanCode,
                expiresAt = granted?.ExpiresAt,
            });
        });

        app.MapPost("/api/payments/monnify/webhook", async (
            HttpRequest request,
            MonnifyOptions options,
            IPaymentProvider provider,
            IPaymentStore payments,
            IEntitlementStore entitlements,
            ILoggerFactory logging) =>
        {
            var log = logging.CreateLogger("Monnify.Webhook");

            // Read as raw text before anything parses it. The signature is over these exact
            // bytes, and a body that has been through a deserialiser and back is a different
            // string with the same meaning — which hashes differently, every time.
            using var reader = new StreamReader(request.Body);
            var rawBody = await reader.ReadToEndAsync();

            var signature = request.Headers[MonnifySignature.Header].ToString();

            if (!MonnifySignature.IsValid(rawBody, signature, options.SecretKey))
            {
                // Sandbox sends no signature at all, so there is an explicit way to allow that
                // — and it is never inferred, because inferring it from a base URL means one
                // settings change silently disables the only thing protecting this endpoint.
                if (!options.AllowUnsignedWebhooks || !string.IsNullOrWhiteSpace(signature))
                {
                    log.LogWarning("Rejected a Monnify webhook with a bad or missing signature.");
                    return Results.Unauthorized();
                }

                log.LogWarning("Accepted an unsigned Monnify webhook because unsigned webhooks are allowed.");
            }

            var reference = MonnifyWebhook.ReferenceIn(rawBody);
            if (reference is null)
            {
                log.LogWarning("A Monnify webhook carried no reference this could act on.");
                // Acknowledged anyway: a 200 stops Monnify retrying something that will never
                // succeed. What it says is "heard", not "agreed".
                return Results.Ok();
            }

            var intent = payments.Find(reference) ?? payments.FindByProvider(reference);
            if (intent is null)
            {
                log.LogWarning("A Monnify webhook named a payment this server does not know.");
                return Results.Ok();
            }

            if (intent.ProviderReference is { Length: > 0 })
                await Settle(intent, provider, payments, entitlements);

            return Results.Ok();
        });
    }

    /// <summary>
    /// Verifies a pending payment and grants what it bought, once.
    ///
    /// The single place either path can grant anything, so the webhook and the callback cannot
    /// disagree, and the same payment reported four times grants one subscription.
    /// </summary>
    private static async Task Settle(
        PaymentIntent intent,
        IPaymentProvider provider,
        IPaymentStore payments,
        IEntitlementStore entitlements)
    {
        VerifiedPayment verified;
        try
        {
            verified = await provider.VerifyAsync(intent.ProviderReference!);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or HttpRequestException or TaskCanceledException)
        {
            // Left pending on purpose. A verification that could not be reached is not a
            // failed payment, and marking it failed would strand money somebody has spent.
            return;
        }

        // The provider is asked about a reference we stored, so a mismatch here means the
        // references have been crossed. Refusing is the only safe answer to that.
        if (verified.OurReference is { Length: > 0 } stated
            && !string.Equals(stated, intent.Reference, StringComparison.Ordinal))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (!intent.Settle(verified, now)) return;

        payments.Save(intent);

        if (intent.Status != PaymentStatus.Paid) return;

        var plan = Plan.Find(intent.PlanCode);
        if (plan is null) return;

        entitlements.Save(Entitlement.Grant(
            entitlements.For(intent.StudentId), intent.StudentId, plan, intent.Reference, now));
    }
}
