using System.Globalization;
using System.Text.Json.Nodes;
using Lumen.Domain.Billing;

namespace Lumen.Infrastructure.Billing;

/// <summary>
/// Reading a Monnify webhook body.
///
/// This is now the only thing that confirms a payment, which makes the signature check the
/// whole security boundary and makes this parse the whole business decision. Both halves are
/// written to fail closed: a body that cannot be read, or that carries no amount, confirms
/// nothing rather than confirming something with a default in it.
///
/// Field lookup is by name at any depth rather than against a fixed shape, because Monnify has
/// moved these between the top level and an <c>eventData</c> object across versions. Binding to
/// one shape means a version bump silently stops granting anybody anything — and silence is the
/// worst failure available here, since the money still leaves the student's account.
/// </summary>
public static class MonnifyWebhook
{
    public static ConfirmedPayment? Read(string rawBody)
    {
        var body = Parse(rawBody);
        if (body is null) return null;

        return new ConfirmedPayment(
            OurReference: Text(body, "paymentReference"),
            ProviderReference: Text(body, "transactionReference"),
            EventType: Text(body, "eventType"),
            Status: Text(body, "paymentStatus"),
            Paid: Amount(body, "amountPaid"),
            Currency: Text(body, "currencyCode") ?? Text(body, "currency"));
    }

    /// <summary>Which payment a message is about, when that is all we need.</summary>
    public static string? ReferenceIn(string rawBody)
    {
        var body = Parse(rawBody);
        if (body is null) return null;

        return Text(body, "paymentReference") ?? Text(body, "transactionReference");
    }

    private static JsonNode? Parse(string rawBody)
    {
        try
        {
            return JsonNode.Parse(rawBody);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// The first value of this name anywhere in the message, breadth first.
    ///
    /// Breadth first on purpose: when the same name appears at the top level and nested, the
    /// outer one is the envelope's and is the one Monnify means.
    /// </summary>
    private static JsonNode? Field(JsonNode? root, string name)
    {
        var queue = new Queue<JsonNode?>();
        queue.Enqueue(root);

        var seen = 0;

        while (queue.Count > 0 && seen++ < 512)
        {
            if (queue.Dequeue() is not JsonObject own) continue;

            if (own[name] is { } found and not JsonObject and not JsonArray) return found;

            foreach (var child in own) queue.Enqueue(child.Value);
        }

        return null;
    }

    private static string? Text(JsonNode? root, string name)
    {
        var field = Field(root, name);
        if (field is not JsonValue value) return null;

        var text = value.TryGetValue<string>(out var asString)
            ? asString
            : value.ToJsonString().Trim('"');

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <summary>
    /// An amount, read from the JSON text rather than through a double.
    ///
    /// Null when it is absent or unreadable, and the caller treats that as "not confirmed".
    /// A missing amount defaulting to zero would confirm a payment of nothing; defaulting to
    /// the price would confirm a payment nobody made.
    /// </summary>
    private static Money? Amount(JsonNode? root, string name)
    {
        if (Text(root, name) is not { } text) return null;

        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var naira))
            return null;

        if (naira < 0) return null;

        return Money.FromNaira(decimal.Round(naira, 2, MidpointRounding.ToEven));
    }
}
