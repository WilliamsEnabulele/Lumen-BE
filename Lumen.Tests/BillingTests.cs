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
public class PaymentConfirmationTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-20T10:00:00Z");

    private static PaymentIntent Pending() => new()
    {
        StudentId = Guid.CreateVersion7(),
        Reference = "lmn_20260920_abc123",
        PlanCode = Plan.Monthly.Code,
        AmountKobo = Plan.Monthly.Price.Kobo,
    };

    private static ConfirmedPayment Says(
        decimal? naira = 2500, string status = "PAID", string currency = "NGN") =>
        new("lmn_20260920_abc123", "MNFY|123", "SUCCESSFUL_TRANSACTION", status,
            naira is null ? null : Money.FromNaira(naira.Value), currency);

    private static WebhookConfirmation Arrived(bool signatureValid = true) =>
        new(Now, "{}", "sig", signatureValid, "SUCCESSFUL_TRANSACTION", "PAID", 250_000, "NGN", "test");

    [Fact]
    public void A_signed_confirmation_for_the_full_price_settles_it()
    {
        var intent = Pending();

        Assert.True(intent.Confirm(Says(), Arrived(), Now));
        Assert.Equal(PaymentStatus.Paid, intent.Status);
        Assert.Equal(Now, intent.SettledAt);
    }

    [Fact]
    public void An_unsigned_confirmation_records_itself_and_changes_nothing()
    {
        // The whole security boundary, now that confirmation arrives one way only.
        var intent = Pending();

        Assert.False(intent.Confirm(Says(), Arrived(signatureValid: false), Now));
        Assert.Equal(PaymentStatus.Pending, intent.Status);
        Assert.Single(intent.Confirmations);
    }

    [Fact]
    public void Every_confirmation_is_recorded_including_the_ones_that_did_nothing()
    {
        // A log that only keeps the decisive message cannot show that four arrived.
        var intent = Pending();

        intent.Confirm(Says(status: "PENDING"), Arrived(), Now);
        intent.Confirm(Says(), Arrived(), Now.AddMinutes(1));
        intent.Confirm(Says(), Arrived(), Now.AddMinutes(2));

        Assert.Equal(3, intent.Confirmations.Count);
        Assert.Equal(PaymentStatus.Paid, intent.Status);
    }

    [Fact]
    public void A_message_carrying_no_amount_confirms_nothing()
    {
        // "They paid" with no number attached is not something to grant a subscription on,
        // and a missing amount defaulting to zero or to the price is worse than refusing.
        var intent = Pending();

        Assert.False(intent.Confirm(Says(naira: null), Arrived(), Now));
        Assert.Equal(PaymentStatus.Pending, intent.Status);
    }

    [Fact]
    public void A_message_that_is_not_a_payment_leaves_it_pending_rather_than_failing_it()
    {
        // The next message may say the money arrived. Closing the payment here would strand
        // a student midway through paying.
        var intent = Pending();

        Assert.False(intent.Confirm(Says(status: "PENDING"), Arrived(), Now));
        Assert.Equal(PaymentStatus.Pending, intent.Status);
    }

    [Fact]
    public void Paying_too_little_buys_nothing_and_is_not_called_a_failure()
    {
        var intent = Pending();

        intent.Confirm(Says(naira: 1000), Arrived(), Now);

        Assert.Equal(PaymentStatus.Underpaid, intent.Status);
        Assert.Contains("1000.00", intent.Outcome!, StringComparison.Ordinal);
    }

    [Fact]
    public void Paying_more_than_the_price_still_pays_the_price()
    {
        var intent = Pending();

        intent.Confirm(Says(naira: 5000), Arrived(), Now);

        Assert.Equal(PaymentStatus.Paid, intent.Status);
    }

    [Fact]
    public void A_payment_in_the_wrong_currency_is_not_a_payment()
    {
        var intent = Pending();

        intent.Confirm(Says(currency: "USD"), Arrived(), Now);

        Assert.Equal(PaymentStatus.Failed, intent.Status);
    }

    [Fact]
    public void The_same_payment_reported_twice_only_grants_once()
    {
        // Monnify retries. This is what makes that harmless.
        var intent = Pending();

        Assert.True(intent.Confirm(Says(), Arrived(), Now));
        Assert.False(intent.Confirm(Says(), Arrived(), Now.AddMinutes(5)));
        Assert.Equal(Now, intent.SettledAt);
        Assert.Equal(2, intent.Confirmations.Count);
    }

    [Fact]
    public void A_settled_underpayment_cannot_be_talked_into_succeeding_later()
    {
        var intent = Pending();
        intent.Confirm(Says(naira: 10), Arrived(), Now);

        Assert.False(intent.Confirm(Says(), Arrived(), Now.AddHours(1)));
        Assert.Equal(PaymentStatus.Underpaid, intent.Status);
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

public class FreeTierTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-20T10:00:00Z");

    private static Entitlement Active() =>
        Entitlement.Grant(null, Guid.CreateVersion7(), Plan.Monthly, "lmn_1", Now);

    [Fact]
    public void With_billing_off_everybody_uploads()
    {
        Assert.True(Access.MayUpload(enforced: false, entitlement: null, uploadsThisMonth: 99, Now));
    }

    [Fact]
    public void The_first_document_of_the_month_is_free()
    {
        Assert.True(Access.MayUpload(enforced: true, entitlement: null, uploadsThisMonth: 0, Now));
    }

    [Fact]
    public void The_second_one_is_not()
    {
        Assert.False(Access.MayUpload(enforced: true, entitlement: null, uploadsThisMonth: 1, Now));
    }

    [Fact]
    public void A_subscriber_is_never_counted()
    {
        Assert.True(Access.MayUpload(enforced: true, Active(), uploadsThisMonth: 40, Now));
    }

    [Fact]
    public void An_expired_subscription_falls_back_to_the_free_allowance_rather_than_to_nothing()
    {
        var lapsed = Entitlement.Grant(null, Guid.CreateVersion7(), Plan.Monthly, "lmn_1", Now.AddDays(-60));

        Assert.True(Access.MayUpload(enforced: true, lapsed, uploadsThisMonth: 0, Now));
        Assert.False(Access.MayUpload(enforced: true, lapsed, uploadsThisMonth: 1, Now));
    }

    [Fact]
    public void What_is_left_never_goes_negative()
    {
        Assert.Equal(1, Access.FreeUploadsLeft(0));
        Assert.Equal(0, Access.FreeUploadsLeft(1));
        Assert.Equal(0, Access.FreeUploadsLeft(7));
    }

    [Fact]
    public void The_allowance_comes_back_at_the_start_of_next_month()
    {
        // Calendar months, because "one a month" is a promise people check against a calendar.
        Assert.Equal(
            DateTimeOffset.Parse("2026-10-01T00:00:00Z"),
            Access.AllowanceResetsAt(DateTimeOffset.Parse("2026-09-30T23:59:00Z")));
    }

    [Fact]
    public void December_rolls_into_January()
    {
        Assert.Equal(
            DateTimeOffset.Parse("2027-01-01T00:00:00Z"),
            Access.AllowanceResetsAt(DateTimeOffset.Parse("2026-12-14T09:00:00Z")));
    }

    [Fact]
    public void An_upload_belongs_to_the_month_it_happened_in()
    {
        var upload = new UploadRecord { At = DateTimeOffset.Parse("2026-09-30T23:30:00Z") };

        Assert.True(upload.IsInMonthOf(DateTimeOffset.Parse("2026-09-01T00:00:00Z")));
        Assert.False(upload.IsInMonthOf(DateTimeOffset.Parse("2026-10-01T00:30:00Z")));
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
