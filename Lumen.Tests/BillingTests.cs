using Lumen.Domain.Billing;
using Lumen.Infrastructure.Billing;

namespace Lumen.Tests;

public class MoneyTests
{
    [Fact]
    public void Naira_become_kobo_exactly()
    {
        Assert.Equal(250_000, Money.FromNaira(2500).Kobo);
        Assert.Equal(250_010, Money.FromNaira(2500.10m).Kobo);
    }

    [Fact]
    public void A_price_finer_than_a_kobo_is_a_bug_in_the_price()
    {
        // Rounding it away would mean the amount charged and the amount checked can differ,
        // and the difference only shows up as a payment that will not settle.
        Assert.Throws<ArgumentOutOfRangeException>(() => Money.FromNaira(10.001m));
    }

    [Fact]
    public void The_wire_format_is_two_places_and_culture_free()
    {
        Assert.Equal("2500.00", Money.FromNaira(2500).ToNairaString());
        Assert.Equal("2500.10", Money.FromNaira(2500.1m).ToNairaString());
    }

    [Fact]
    public void Covering_a_price_means_at_least_it()
    {
        var price = Money.FromNaira(2500);

        Assert.True(Money.FromNaira(2500).Covers(price));
        Assert.True(Money.FromNaira(2500.01m).Covers(price));
        Assert.False(Money.FromNaira(2499.99m).Covers(price));
    }
}

/// <summary>
/// The state machine a payment moves through. Every test here is a way somebody gets something
/// for nothing, or pays and gets nothing.
/// </summary>
public class PaymentIntentTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-20T10:00:00Z");

    private static PaymentIntent Pending() => new()
    {
        StudentId = Guid.CreateVersion7(),
        Reference = "lmn_20260920_abc123",
        PlanCode = Plan.Monthly.Code,
        AmountKobo = Plan.Monthly.Price.Kobo,
    };

    private static VerifiedPayment Paid(decimal naira, bool succeeded = true, string currency = "NGN") =>
        new("MNFY|123", "lmn_20260920_abc123", Money.FromNaira(naira), currency, succeeded, succeeded ? "PAID" : "FAILED");

    [Fact]
    public void Paying_the_price_settles_it()
    {
        var intent = Pending();

        Assert.True(intent.Settle(Paid(2500), Now));
        Assert.Equal(PaymentStatus.Paid, intent.Status);
        Assert.Equal(Now, intent.SettledAt);
    }

    [Fact]
    public void Paying_too_little_buys_nothing()
    {
        // And is not recorded as a failure, because real money is sitting there and somebody
        // is owed either the rest of the service or it back.
        var intent = Pending();

        intent.Settle(Paid(1000), Now);

        Assert.Equal(PaymentStatus.Underpaid, intent.Status);
        Assert.Contains("1000.00", intent.Outcome!, StringComparison.Ordinal);
    }

    [Fact]
    public void Paying_more_than_the_price_still_pays_the_price()
    {
        var intent = Pending();

        intent.Settle(Paid(5000), Now);

        Assert.Equal(PaymentStatus.Paid, intent.Status);
    }

    [Fact]
    public void A_payment_in_the_wrong_currency_is_not_a_payment()
    {
        var intent = Pending();

        intent.Settle(Paid(2500, currency: "USD"), Now);

        Assert.Equal(PaymentStatus.Failed, intent.Status);
    }

    [Fact]
    public void A_provider_status_that_is_not_success_never_settles_as_paid()
    {
        var intent = Pending();

        intent.Settle(Paid(2500, succeeded: false), Now);

        Assert.Equal(PaymentStatus.Failed, intent.Status);
    }

    [Fact]
    public void The_same_payment_reported_twice_only_moves_once()
    {
        // The whole reason the flow is idempotent: Monnify retries, the student refreshes the
        // callback, and both paths run through here.
        var intent = Pending();

        Assert.True(intent.Settle(Paid(2500), Now));
        Assert.False(intent.Settle(Paid(2500), Now.AddMinutes(5)));
        Assert.Equal(Now, intent.SettledAt);
    }

    [Fact]
    public void A_settled_failure_cannot_be_talked_into_succeeding_later()
    {
        var intent = Pending();
        intent.Settle(Paid(2500, succeeded: false), Now);

        Assert.False(intent.Settle(Paid(2500), Now.AddHours(1)));
        Assert.Equal(PaymentStatus.Failed, intent.Status);
    }
}

public class EntitlementTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-20T10:00:00Z");

    [Fact]
    public void A_first_payment_grants_from_today()
    {
        var granted = Entitlement.Grant(null, Guid.CreateVersion7(), Plan.Monthly, "lmn_1", Now);

        Assert.Equal(Now.AddDays(30), granted.ExpiresAt);
        Assert.True(granted.IsActiveAt(Now.AddDays(29)));
        Assert.False(granted.IsActiveAt(Now.AddDays(31)));
    }

    [Fact]
    public void Renewing_early_adds_to_the_time_left_rather_than_replacing_it()
    {
        // Overwriting would charge somebody for days they then lose, which is the kind of
        // quiet theft nobody notices until they do.
        var student = Guid.CreateVersion7();
        var first = Entitlement.Grant(null, student, Plan.Monthly, "lmn_1", Now);

        var renewed = Entitlement.Grant(first, student, Plan.Monthly, "lmn_2", Now.AddDays(10));

        Assert.Equal(Now.AddDays(60), renewed.ExpiresAt);
    }

    [Fact]
    public void Paying_again_after_lapsing_starts_from_today()
    {
        var student = Guid.CreateVersion7();
        var lapsed = Entitlement.Grant(null, student, Plan.Monthly, "lmn_1", Now);

        var renewed = Entitlement.Grant(lapsed, student, Plan.Monthly, "lmn_2", Now.AddDays(45));

        Assert.Equal(Now.AddDays(75), renewed.ExpiresAt);
    }
}

public class AccessTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-20T10:00:00Z");

    [Fact]
    public void With_billing_off_everybody_learns()
    {
        Assert.True(Access.MayLearn(enforced: false, entitlement: null, Now));
    }

    [Fact]
    public void With_billing_on_nobody_learns_without_one()
    {
        Assert.False(Access.MayLearn(enforced: true, entitlement: null, Now));
    }

    [Fact]
    public void An_expired_entitlement_is_not_an_entitlement()
    {
        var lapsed = Entitlement.Grant(null, Guid.CreateVersion7(), Plan.Monthly, "lmn_1", Now.AddDays(-60));

        Assert.False(Access.MayLearn(enforced: true, lapsed, Now));
        Assert.True(Access.MayLearn(enforced: false, lapsed, Now));
    }
}

public class PaymentReferenceTests
{
    [Fact]
    public void References_are_unique_and_ours()
    {
        var references = Enumerable.Range(0, 200).Select(_ => PaymentReference.Next()).ToArray();

        Assert.Equal(references.Length, references.Distinct(StringComparer.Ordinal).Count());
        Assert.All(references, reference => Assert.True(PaymentReference.IsWellFormed(reference)));
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("lmn_../x")]
    [InlineData("MNFY|12345")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_that_could_be_a_path_is_not_well_formed(string? candidate)
    {
        // The reference arrives from outside in both the webhook and the callback, and is used
        // to look a payment up. A shape check before the lookup is the cheap half of that.
        Assert.False(PaymentReference.IsWellFormed(candidate));
    }
}
