namespace Lumen.Domain.Billing;

/// <summary>
/// Whether a student may be taught.
///
/// One line, kept apart from the endpoint that asks it, because it is the line that decides
/// whether the product is free — and a rule that important should be visible and tested rather
/// than inlined in a handler where flipping it is a one-character edit nobody reviews.
/// </summary>
public static class Access
{
    public static bool MayLearn(bool enforced, Entitlement? entitlement, DateTimeOffset now) =>
        !enforced || (entitlement?.IsActiveAt(now) ?? false);
}
