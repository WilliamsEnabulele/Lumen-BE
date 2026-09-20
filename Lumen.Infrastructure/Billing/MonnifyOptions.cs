namespace Lumen.Infrastructure.Billing;

public sealed class MonnifyOptions
{
    public const string Section = "Billing:Monnify";

    /// <summary>Sandbox by default. Going live is a settings change somebody makes deliberately.</summary>
    public string BaseUrl { get; set; } = "https://sandbox.monnify.com";

    public string? ApiKey { get; set; }

    /// <summary>Also the key the webhook signature is computed with. Never leaves the server.</summary>
    public string? SecretKey { get; set; }

    public string? ContractCode { get; set; }

    /// <summary>Where Monnify sends the student back to once they are done paying.</summary>
    public string RedirectUrl { get; set; } = "http://localhost:4200/paid";

    public string[] PaymentMethods { get; set; } = ["CARD", "ACCOUNT_TRANSFER", "USSD"];

    /// <summary>
    /// Sandbox does not send the signature header — only production does. This allows the
    /// unsigned ones through, and it is separate from <see cref="IsConfigured"/> so that
    /// turning it on is an explicit act that reads as dangerous in a settings file, rather
    /// than something inferred from a base URL that somebody will later change.
    /// </summary>
    public bool AllowUnsignedWebhooks { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(SecretKey)
        && !string.IsNullOrWhiteSpace(ContractCode);
}
