using System.Text.Json.Nodes;

namespace Lumen.Infrastructure.Billing;

/// <summary>
/// Reading a Monnify webhook body.
///
/// Kept deliberately tiny, because almost nothing in the body is trusted. The signature says
/// the message is genuine; it does not make the numbers inside it true, and a payment is
/// settled from what Monnify says when asked directly. So all this has to find is which
/// payment the message is about.
/// </summary>
public static class MonnifyWebhook
{
    /// <summary>
    /// Pulls a payment reference out of a webhook body, wherever it sits.
    ///
    /// Deliberately shallow. Monnify has moved this field between the top level and an
    /// <c>eventData</c> object across versions, and binding to one shape means a version bump
    /// silently stops granting anybody anything. Nothing else is read from the body at all —
    /// not the amount, not the status — so being relaxed about its shape costs nothing: the
    /// reference only decides which payment to go and ask Monnify about.
    /// </summary>
    public static string? ReferenceIn(string rawBody)
    {
        JsonNode? body;
        try
        {
            body = JsonNode.Parse(rawBody);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or ArgumentException)
        {
            return null;
        }

        return Find(body, depth: 0);

        static string? Find(JsonNode? node, int depth)
        {
            if (node is not JsonObject own || depth > 4) return null;

            foreach (var name in (string[])["paymentReference", "transactionReference"])
            {
                if (own[name] is JsonValue value
                    && value.TryGetValue<string>(out var reference)
                    && !string.IsNullOrWhiteSpace(reference))
                {
                    return reference;
                }
            }

            foreach (var child in own)
            {
                if (Find(child.Value, depth + 1) is { } found) return found;
            }

            return null;
        }
    }
}
