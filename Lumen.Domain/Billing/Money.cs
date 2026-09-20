using System.Globalization;

namespace Lumen.Domain.Billing;

/// <summary>
/// An amount of money, held in minor units.
///
/// Kobo rather than naira, and a long rather than a decimal, because every comparison this
/// type exists for is an equality or a "did they pay enough" — and those are the two questions
/// a floating point amount answers wrongly just often enough to be missed in testing. The
/// provider speaks naira with two decimal places; the conversion happens once, here, where it
/// can be tested, rather than at every call site.
/// </summary>
public readonly record struct Money(long Kobo)
{
    public static readonly Money Zero = new(0);

    public decimal Naira => Kobo / 100m;

    /// <summary>
    /// Naira as the provider states them. Rejects fractions of a kobo rather than rounding
    /// them away: a price that cannot be represented exactly is a bug in the price, and
    /// silently rounding it means the amount charged and the amount checked can differ.
    /// </summary>
    public static Money FromNaira(decimal naira)
    {
        var kobo = naira * 100m;

        if (kobo != decimal.Truncate(kobo))
            throw new ArgumentOutOfRangeException(
                nameof(naira), naira, "Amounts are exact to the kobo; this one is not.");

        return new Money((long)kobo);
    }

    /// <summary>What goes on the wire to the provider: naira, two decimal places, invariant.</summary>
    public string ToNairaString() => Naira.ToString("0.00", CultureInfo.InvariantCulture);

    public bool Covers(Money price) => Kobo >= price.Kobo;

    public override string ToString() => $"₦{ToNairaString()}";
}
