namespace Lumen.Domain.Common;

/// <summary>Base for all persisted aggregates. Ids are assigned by the application, not the database.</summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>
    /// Present from the first migration rather than added when the first institution signs.
    /// Retrofitting tenancy is a migration through every table, every query and every index,
    /// at exactly the moment a customer is watching. A column and a filter now is the cheap
    /// half of that trade.
    /// </summary>
    public Guid TenantId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
