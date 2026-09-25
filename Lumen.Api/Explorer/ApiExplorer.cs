using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Lumen.Api.Explorer;

/// <summary>
/// Group names for the explorer, in one place so the same group cannot arrive under two
/// spellings and show up as two groups.
/// </summary>
public static class ApiTags
{
    public const string Service = "Service";
    public const string Accounts = "Accounts";
    public const string Courses = "Courses";
    public const string Teaching = "Teaching";
    public const string Billing = "Billing";
}

/// <summary>
/// What the generated OpenAPI document says about authentication.
///
/// The document is produced from the endpoints themselves, so most of it needs no help. The
/// one thing it cannot work out is the bearer token: these endpoints check <c>ISignedIn</c>
/// inside the handler rather than declaring an authorization policy, because a policy would
/// answer with the middleware's empty 401 instead of the sentence each handler writes — and
/// those sentences are what the client shows the student.
///
/// The cost of that choice is that nothing in the endpoint metadata says "this needs a token",
/// so it is said here.
/// </summary>
internal static class ApiExplorer
{
    /// <summary>The name the scheme is declared under, and referred to by from each operation.</summary>
    internal const string SchemeName = "bearer";

    /// <summary>
    /// Declares the scheme once, on the document, which is what puts the Authorize button in
    /// the UI and lets a token be pasted in one place rather than per request.
    /// </summary>
    internal static Task DeclareBearerScheme(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

        document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description =
                "The accessToken from /api/auth/login or /api/auth/register, which is good for "
                + "fifteen minutes. Paste the value on its own — the UI adds the word Bearer. "
                + "The refresh token is deliberately not here: it lives in an HttpOnly cookie "
                + "that the browser sends to /api/auth by itself and that no script can read.",
        };

        return Task.CompletedTask;
    }

    /// <summary>
    /// Says which operations need that token.
    ///
    /// Every one of them, unless its endpoint has said otherwise with <c>AllowAnonymous</c>.
    /// The default runs in the safe direction on purpose: an endpoint added later without a
    /// thought is documented as needing a token, and the worst that mistake can do is ask
    /// somebody for one they did not need. The opposite default publishes a protected endpoint
    /// as though it were open, and the person who believes it is the one writing a client.
    /// </summary>
    internal static Task RequireBearerUnlessAnonymous(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var anonymous = context.Description.ActionDescriptor.EndpointMetadata
            .OfType<IAllowAnonymous>()
            .Any();

        if (anonymous) return Task.CompletedTask;

        operation.Security =
        [
            new OpenApiSecurityRequirement
            {
                // No scopes: this is a bearer token that either verifies or does not, not an
                // OAuth grant with a list of things it is allowed to do.
                [new OpenApiSecuritySchemeReference(SchemeName, context.Document)] = [],
            },
        ];

        return Task.CompletedTask;
    }
}
