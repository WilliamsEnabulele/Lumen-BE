using Lumen.Infrastructure.Billing;

namespace Lumen.Tests;

/// <summary>
/// The signature check, which is the only thing standing between this endpoint and anybody who
/// learns its URL granting themselves a subscription.
/// </summary>
public class MonnifySignatureTests
{
    private const string Secret = "TEST_SECRET_KEY";
    private const string Body = """{"eventType":"SUCCESSFUL_TRANSACTION","eventData":{"paymentReference":"lmn_1"}}""";

    [Fact]
    public void A_body_signed_with_the_secret_is_accepted()
    {
        Assert.True(MonnifySignature.IsValid(Body, MonnifySignature.Compute(Body, Secret), Secret));
    }

    [Fact]
    public void The_hash_is_hmac_sha512_hex_lowercase()
    {
        var signature = MonnifySignature.Compute(Body, Secret);

        // 512 bits is 64 bytes is 128 hex characters. A different length means a different
        // algorithm, which is the failure that looks like "the signature never matches".
        Assert.Equal(128, signature.Length);
        Assert.Equal(signature.ToLowerInvariant(), signature);
        Assert.Matches("^[0-9a-f]+$", signature);
    }

    [Fact]
    public void A_signature_from_a_different_secret_is_refused()
    {
        Assert.False(MonnifySignature.IsValid(Body, MonnifySignature.Compute(Body, "SOMEBODY_ELSE"), Secret));
    }

    [Fact]
    public void One_changed_byte_in_the_body_refuses_it()
    {
        var signature = MonnifySignature.Compute(Body, Secret);
        var tampered = Body.Replace("lmn_1", "lmn_2", StringComparison.Ordinal);

        Assert.False(MonnifySignature.IsValid(tampered, signature, Secret));
    }

    [Fact]
    public void Re_serialising_the_body_changes_the_hash()
    {
        // The reason the endpoint reads the raw stream rather than a bound model. This is not a
        // subtle preference: whitespace and key order survive in the bytes and not in an object,
        // so a re-serialised body fails the check every single time.
        var reordered = """{"eventData":{"paymentReference":"lmn_1"},"eventType":"SUCCESSFUL_TRANSACTION"}""";

        Assert.NotEqual(MonnifySignature.Compute(Body, Secret), MonnifySignature.Compute(reordered, Secret));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-hash")]
    public void A_missing_or_malformed_signature_is_refused(string? signature)
    {
        Assert.False(MonnifySignature.IsValid(Body, signature, Secret));
    }

    [Fact]
    public void With_no_secret_configured_nothing_verifies()
    {
        // Fails closed. The alternative — treating an unset secret as "skip the check" — is a
        // misconfiguration that silently opens the endpoint.
        Assert.False(MonnifySignature.IsValid(Body, MonnifySignature.Compute(Body, Secret), null));
    }

    [Fact]
    public void Case_in_the_signature_does_not_matter()
    {
        var upper = MonnifySignature.Compute(Body, Secret).ToUpperInvariant();

        Assert.True(MonnifySignature.IsValid(Body, upper, Secret));
    }
}

/// <summary>
/// Finding the payment a webhook is about. Nothing else is read from the body — not the
/// amount, not the status — so this is the whole of what the payload is trusted for.
/// </summary>
public class WebhookReferenceTests
{
    [Fact]
    public void A_reference_at_the_top_level_is_found()
    {
        Assert.Equal("lmn_1", MonnifyWebhook.ReferenceIn("""{"paymentReference":"lmn_1"}"""));
    }

    [Fact]
    public void A_reference_nested_under_event_data_is_found()
    {
        // Monnify has moved this between shapes across versions. Binding to one means a version
        // bump silently stops granting anybody anything.
        Assert.Equal("lmn_1", MonnifyWebhook.ReferenceIn(
            """{"eventType":"SUCCESSFUL_TRANSACTION","eventData":{"paymentReference":"lmn_1"}}"""));
    }

    [Fact]
    public void Our_own_reference_wins_over_the_providers()
    {
        Assert.Equal("lmn_1", MonnifyWebhook.ReferenceIn(
            """{"eventData":{"transactionReference":"MNFY|99","paymentReference":"lmn_1"}}"""));
    }

    [Fact]
    public void A_provider_reference_alone_is_still_something_to_look_up()
    {
        Assert.Equal("MNFY|99", MonnifyWebhook.ReferenceIn("""{"eventData":{"transactionReference":"MNFY|99"}}"""));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{}")]
    [InlineData("""{"eventType":"SETTLEMENT"}""")]
    [InlineData("[]")]
    public void A_body_with_nothing_to_act_on_yields_nothing(string body)
    {
        Assert.Null(MonnifyWebhook.ReferenceIn(body));
    }
}
