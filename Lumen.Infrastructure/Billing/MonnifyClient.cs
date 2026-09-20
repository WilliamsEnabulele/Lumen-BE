using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lumen.Domain.Billing;
using Microsoft.Extensions.Logging;

namespace Lumen.Infrastructure.Billing;

/// <summary>Starting a payment: where to send the student, and what the provider called it.</summary>
public sealed record StartedPayment(string CheckoutUrl, string ProviderReference);

/// <summary>
/// Talks to Monnify.
///
/// One call: start a transaction and get back a URL to send somebody to. Nothing in this file
/// decides whether a payment happened — that arrives as a signed webhook and is read in
/// <see cref="MonnifyWebhook"/>, so nothing here can grant anybody anything.
/// </summary>
public sealed class MonnifyClient(
    HttpClient http,
    MonnifyOptions options,
    ILogger<MonnifyClient> logger) : IPaymentProvider
{
    public string Name => "monnify";

    private string? _token;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    public async Task<StartedPayment> StartAsync(
        PaymentIntent intent, string customerName, string customerEmail, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);

        var body = new JsonObject
        {
            // Naira with two decimal places, formatted once in Money so the amount sent and the
            // amount checked on the way back cannot drift apart.
            ["amount"] = decimal.Parse(intent.Amount.ToNairaString(), System.Globalization.CultureInfo.InvariantCulture),
            ["customerName"] = customerName,
            ["customerEmail"] = customerEmail,
            ["paymentReference"] = intent.Reference,
            ["paymentDescription"] = Plan.Find(intent.PlanCode)?.Name ?? "Lumen",
            ["currencyCode"] = intent.Currency,
            ["contractCode"] = options.ContractCode,
            ["redirectUrl"] = options.RedirectUrl,
            ["paymentMethods"] = new JsonArray([.. options.PaymentMethods.Select(method => (JsonNode)method!)]),
        };

        var response = await SendAsync(
            HttpMethod.Post, "/api/v1/merchant/transactions/init-transaction", body, cancellationToken);

        var checkoutUrl = response["checkoutUrl"]?.GetValue<string>();
        var providerReference = response["transactionReference"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(checkoutUrl) || string.IsNullOrWhiteSpace(providerReference))
            throw new InvalidOperationException("Monnify accepted the transaction but returned no checkout URL.");

        logger.LogInformation("Started payment {Reference} for {Amount}.", intent.Reference, intent.Amount);

        return new StartedPayment(checkoutUrl, providerReference);
    }

    /// <summary>
    /// One request, with the token attached, the envelope unwrapped and failures named.
    ///
    /// Monnify answers 200 with <c>requestSuccessful: false</c> for business failures, so the
    /// HTTP status alone says nothing. Trusting it would have a declined payment parse as an
    /// empty success.
    /// </summary>
    private async Task<JsonNode> SendAsync(
        HttpMethod method, string path, JsonNode? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, options.BaseUrl.TrimEnd('/') + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync(cancellationToken));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (body is not null)
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await http.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        return Unwrap(payload, $"{method} {path}");
    }

    private static JsonNode Unwrap(string payload, string what)
    {
        JsonNode? envelope;
        try
        {
            envelope = JsonNode.Parse(payload);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException($"Monnify answered {what} with something that is not JSON.");
        }

        if (envelope is null)
            throw new InvalidOperationException($"Monnify answered {what} with nothing.");

        if (envelope["requestSuccessful"]?.GetValue<bool>() != true)
        {
            var message = envelope["responseMessage"]?.GetValue<string>() ?? "no reason given";
            var code = envelope["responseCode"]?.GetValue<string>() ?? "?";
            throw new InvalidOperationException($"Monnify refused {what}: {message} ({code}).");
        }

        return envelope["responseBody"]
               ?? throw new InvalidOperationException($"Monnify answered {what} with no body.");
    }

    /// <summary>
    /// A bearer token, cached until shortly before it expires.
    ///
    /// Tokens last an hour, so fetching one per request triples the calls and the latency for
    /// no benefit. Renewed a minute early because a token that expires in flight fails the
    /// request it was attached to, and that request may be the one granting somebody access.
    /// </summary>
    private async Task<string> TokenAsync(CancellationToken cancellationToken)
    {
        if (_token is not null && DateTimeOffset.UtcNow < _tokenExpiresAt) return _token;

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_token is not null && DateTimeOffset.UtcNow < _tokenExpiresAt) return _token;

            using var request = new HttpRequestMessage(HttpMethod.Post, options.BaseUrl.TrimEnd('/') + "/api/v1/auth/login");

            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{options.ApiKey}:{options.SecretKey}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new StringContent(string.Empty, Encoding.UTF8, "application/json");

            using var response = await http.SendAsync(request, cancellationToken);
            var body = Unwrap(await response.Content.ReadAsStringAsync(cancellationToken), "login");

            var token = body["accessToken"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("Monnify logged in but returned no access token.");

            var seconds = body["expiresIn"]?.GetValue<int>() ?? 3600;

            _token = token;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, seconds) - 60);

            return token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }
}

/// <summary>
/// The payment provider, as the rest of the system needs it.
///
/// An interface so the endpoints depend on "somebody can start a payment" rather than on
/// Monnify, and so a server with no credentials refuses clearly instead of failing to start.
/// </summary>
public interface IPaymentProvider
{
    string Name { get; }

    Task<StartedPayment> StartAsync(
        PaymentIntent intent, string customerName, string customerEmail, CancellationToken cancellationToken = default);
}
