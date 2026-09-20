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

            // Saved before the provider is called, so a webhook that arrives before this
            // returns still finds a payment to attach itself to.
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
        app.MapGet("/api/payments/{reference}", (string reference, IPaymentStore payments) =>
        {
            if (!PaymentReference.IsWellFormed(reference)) return Results.NotFound();

            var intent = payments.Find(reference);
            if (intent is null) return Results.NotFound();

            // Reports what the webhook has already confirmed. It does not go and ask, because
            // confirmation arrives one way only — so a student who lands here before Monnify's
            // message does sees "pending", which is the truth, rather than a second opinion.
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

        app.MapGet("/api/entitlement", (
            IEntitlementStore entitlements, IUploadLedger uploads, BillingEnforcement billing) =>
        {
            var now = DateTimeOffset.UtcNow;
            var granted = entitlements.For(DefaultStudent);
            var used = uploads.CountInMonth(DefaultStudent, now);

            return Results.Ok(new
            {
                active = granted?.IsActiveAt(now) ?? false,
                plan = granted?.PlanCode,
                expiresAt = granted?.ExpiresAt,
                enforced = billing.Enforced,
                freeUploadsLeft = Access.FreeUploadsLeft(used),
                freeAllowanceResetsAt = Access.AllowanceResetsAt(now),
            });
        });

        app.MapPost("/api/payments/monnify/webhook", async (
            HttpRequest request,
            MonnifyOptions options,
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
            var signed = MonnifySignature.IsValid(rawBody, signature, options.SecretKey);

            // Sandbox sends no signature at all. Allowing that is explicit, never inferred from
            // a base URL, and never extends to a signature that is present and wrong — that is
            // not an unsigned message, it is a forged one.
            var accepted = signed
                || (options.AllowUnsignedWebhooks && string.IsNullOrWhiteSpace(signature));

            if (!accepted)
            {
                log.LogWarning("Rejected a Monnify webhook with a bad or missing signature.");
                return Results.Unauthorized();
            }

            var confirmed = MonnifyWebhook.Read(rawBody);
            var reference = confirmed?.OurReference ?? confirmed?.ProviderReference;

            if (confirmed is null || reference is null)
            {
                log.LogWarning("A Monnify webhook carried nothing this could act on.");
                // Acknowledged anyway: a 200 stops Monnify retrying something that will never
                // succeed. It says "heard", not "agreed".
                return Results.Ok();
            }

            var intent = payments.Find(reference) ?? payments.FindByProvider(reference);
            if (intent is null)
            {
                log.LogWarning("A Monnify webhook named a payment this server does not know.");
                return Results.Ok();
            }

            var now = DateTimeOffset.UtcNow;

            var record = new WebhookConfirmation(
                ReceivedAt: now,
                RawBody: rawBody,
                Signature: string.IsNullOrWhiteSpace(signature) ? null : signature,
                SignatureValid: accepted,
                EventType: confirmed.EventType,
                ProviderStatus: confirmed.Status,
                PaidKobo: confirmed.Paid?.Kobo,
                Currency: confirmed.Currency,
                Outcome: Describe(intent, confirmed));

            var settled = intent.Confirm(confirmed, record, now);

            // Saved either way. The confirmation is the evidence, and evidence that only gets
            // written down when it changed something is evidence nobody can audit.
            payments.Save(intent);

            if (settled && intent.Status == PaymentStatus.Paid && Plan.Find(intent.PlanCode) is { } plan)
            {
                entitlements.Save(Entitlement.Grant(
                    entitlements.For(intent.StudentId), intent.StudentId, plan, intent.Reference, now));

                log.LogInformation("Payment {Reference} confirmed and access granted.", intent.Reference);
            }

            return Results.Ok();
        });
    }

    /// <summary>One line saying what this message meant for this payment, written as it is read.</summary>
    private static string Describe(PaymentIntent intent, ConfirmedPayment confirmed)
    {
        if (intent.IsSettled) return $"Already {intent.Status}; recorded and ignored.";
        if (!confirmed.SaysPaid) return $"Reported {confirmed.Status ?? confirmed.EventType ?? "nothing"}; not a payment.";
        return $"Reported {confirmed.Paid} paid.";
    }
}
