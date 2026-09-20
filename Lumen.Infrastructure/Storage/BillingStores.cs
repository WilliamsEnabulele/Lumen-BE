using System.Collections.Concurrent;
using Lumen.Domain.Billing;

namespace Lumen.Infrastructure.Storage;

public interface IPaymentStore
{
    void Save(PaymentIntent intent);
    PaymentIntent? Find(string reference);

    /// <summary>
    /// By the provider's reference, because that is the one a webhook is most likely to carry.
    /// </summary>
    PaymentIntent? FindByProvider(string providerReference);
}

public interface IEntitlementStore
{
    void Save(Entitlement entitlement);
    Entitlement? For(Guid studentId);
}

/// <summary>
/// Payments on disk.
///
/// The one kind of record here that must never be lost, and the only one where losing it costs
/// somebody money rather than time: a payment taken and then forgotten is a student who paid
/// and has nothing, which they will notice and be right about.
/// </summary>
public sealed class FilePaymentStore : IPaymentStore
{
    private readonly ConcurrentDictionary<string, PaymentIntent> _byReference = new(StringComparer.Ordinal);
    private readonly string _root;

    public FilePaymentStore(string root)
    {
        _root = Path.Combine(root, "payments");
        Directory.CreateDirectory(_root);

        foreach (var intent in JsonFiles.ReadAll<PaymentIntent>(_root)) _byReference[intent.Reference] = intent;
    }

    public void Save(PaymentIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);

        _byReference[intent.Reference] = intent;

        // Filed under the record's id rather than our reference, for the same reason mastery is:
        // a value that arrives from outside must never be spliced into a path.
        JsonFiles.Write(Path.Combine(_root, $"{intent.Id}.json"), intent);
    }

    public PaymentIntent? Find(string reference) =>
        _byReference.TryGetValue(reference, out var intent) ? intent : null;

    public PaymentIntent? FindByProvider(string providerReference) =>
        _byReference.Values.FirstOrDefault(intent =>
            string.Equals(intent.ProviderReference, providerReference, StringComparison.Ordinal));
}

public sealed class FileEntitlementStore : IEntitlementStore
{
    private readonly ConcurrentDictionary<Guid, Entitlement> _byStudent = new();
    private readonly string _root;

    public FileEntitlementStore(string root)
    {
        _root = Path.Combine(root, "entitlements");
        Directory.CreateDirectory(_root);

        foreach (var granted in JsonFiles.ReadAll<Entitlement>(_root)) _byStudent[granted.StudentId] = granted;
    }

    public void Save(Entitlement entitlement)
    {
        ArgumentNullException.ThrowIfNull(entitlement);

        _byStudent[entitlement.StudentId] = entitlement;
        JsonFiles.Write(Path.Combine(_root, $"{entitlement.Id}.json"), entitlement);
    }

    public Entitlement? For(Guid studentId) =>
        _byStudent.TryGetValue(studentId, out var entitlement) ? entitlement : null;
}
