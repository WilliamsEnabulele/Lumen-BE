using System.Text.Json.Serialization;
using Anthropic;
using Lumen.Api.Endpoints;
using Lumen.Api.Explorer;
using Lumen.Domain.Assessment;
using Lumen.Domain.Teaching;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Lumen.Api.Accounts;
using Lumen.Infrastructure.Ai;
using Lumen.Infrastructure.Billing;
using Lumen.Infrastructure.Extraction;
using Lumen.Infrastructure.Ingestion;
using Lumen.Infrastructure.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var dataRoot = builder.Configuration["Storage:Root"]
               ?? Path.Combine(builder.Environment.ContentRootPath, ".local-storage");

builder.Services.AddSingleton<IDocumentExtractor, PlainTextExtractor>();
builder.Services.AddSingleton<IDocumentExtractor, WordExtractor>();
builder.Services.AddSingleton<IDocumentExtractor, SlidesExtractor>();
builder.Services.AddSingleton<IDocumentExtractor, PdfExtractor>();
builder.Services.AddSingleton<DocumentExtractors>();
builder.Services.AddSingleton<ICourseStore>(_ => new FileCourseStore(dataRoot));
builder.Services.AddSingleton<IUploadStorage>(_ => new LocalDiskUploadStorage(dataRoot));
builder.Services.AddSingleton<IStudentStore>(_ => new FileStudentStore(dataRoot));
builder.Services.AddSingleton<IAuthSessionStore>(_ => new FileAuthSessionStore(dataRoot));
builder.Services.AddSingleton<IPaymentStore>(_ => new FilePaymentStore(dataRoot));
builder.Services.AddSingleton<IEntitlementStore>(_ => new FileEntitlementStore(dataRoot));
builder.Services.AddSingleton<IUploadLedger>(_ => new FileUploadLedger(dataRoot));
builder.Services.AddSingleton<ISessionStore>(_ => new FileSessionStore(dataRoot));
builder.Services.AddSingleton<IMasteryStore>(_ => new FileMasteryStore(dataRoot));
builder.Services.AddSingleton<IStudyNoteStore>(_ => new FileStudyNoteStore(dataRoot));
builder.Services.AddSingleton<IngestionPipeline>();

// The AI layer. Reading the document, teaching from it and marking an answer are three
// separate purchases, so they are three separate choices; the deterministic trio behind them
// is a degraded mode that keeps the upload path runnable without a key, not a second
// implementation of the product.
var ai = builder.Configuration.GetSection(AnthropicOptions.Section).Get<AnthropicOptions>() ?? new AnthropicOptions();
ai.ApiKey ??= builder.Configuration["Ai:ApiKey"] ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
builder.Services.AddSingleton(ai);

var google = builder.Configuration.GetSection(GoogleOptions.Section).Get<GoogleOptions>() ?? new GoogleOptions();
google.ApiKey ??= Environment.GetEnvironmentVariable("GOOGLE_API_KEY")
                  ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");
builder.Services.AddSingleton(google);

var routing = builder.Configuration.GetSection(AiRouting.Section).Get<AiRouting>() ?? new AiRouting();
var routed = builder.Configuration.GetSection(AiRouting.Section).Exists();

// Resolved before anything is registered, so a bad choice is a startup failure with a readable
// message rather than a null reference on the first upload.
var author = AiWiring.Resolve(AiRole.Author, routing.Author, routed, ai.IsConfigured, google.IsConfigured);
var tutor = AiWiring.Resolve(AiRole.Tutor, routing.Tutor, routed, ai.IsConfigured, google.IsConfigured);
var judge = AiWiring.Resolve(AiRole.Judge, routing.Judge, routed, ai.IsConfigured, google.IsConfigured);

if (ai.IsConfigured) builder.Services.AddSingleton(_ => new AnthropicClient { ApiKey = ai.ApiKey });
if (author == AiProvider.Google) builder.Services.AddHttpClient<GeminiLessonAuthor>();

builder.Services.AddSingleton<ILessonAuthor>(services => author switch
{
    AiProvider.Anthropic => ActivatorUtilities.CreateInstance<AnthropicLessonAuthor>(services),
    AiProvider.Google => services.GetRequiredService<GeminiLessonAuthor>(),
    _ => ActivatorUtilities.CreateInstance<DeterministicLessonAuthor>(services),
});

builder.Services.AddSingleton<ITutorBrain>(services => tutor switch
{
    AiProvider.Anthropic => ActivatorUtilities.CreateInstance<AnthropicTutorBrain>(services),
    _ => ActivatorUtilities.CreateInstance<ScriptedTutorBrain>(services),
});

builder.Services.AddSingleton<IAnswerJudge>(services => judge switch
{
    AiProvider.Anthropic => ActivatorUtilities.CreateInstance<AnthropicAnswerJudge>(services),
    _ => ActivatorUtilities.CreateInstance<UnjudgedAnswers>(services),
});

// Payments. A server with credentials charges; one without says so plainly rather than
// pretending to take money, and teaching stays open because a paywall nobody can pay through
// is just a closed door.
var monnify = builder.Configuration.GetSection(MonnifyOptions.Section).Get<MonnifyOptions>() ?? new MonnifyOptions();
monnify.ApiKey ??= Environment.GetEnvironmentVariable("MONNIFY_API_KEY");
monnify.SecretKey ??= Environment.GetEnvironmentVariable("MONNIFY_SECRET_KEY");
monnify.ContractCode ??= Environment.GetEnvironmentVariable("MONNIFY_CONTRACT_CODE");
builder.Services.AddSingleton(monnify);

var billing = builder.Configuration.GetSection(BillingOptions.Section).Get<BillingOptions>() ?? new BillingOptions();
var enforceBilling = billing.Enforce ?? monnify.IsConfigured;
builder.Services.AddSingleton(new BillingEnforcement(enforceBilling));

if (monnify.IsConfigured)
{
    builder.Services.AddHttpClient<MonnifyClient>();
    builder.Services.AddSingleton<IPaymentProvider>(services => services.GetRequiredService<MonnifyClient>());
}
else
{
    builder.Services.AddSingleton<IPaymentProvider, PaymentsUnavailable>();
}

// Who is asking. Scoped, so the token is read once per request rather than once per handler
// that wants to know.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ISignedIn, SignedIn>();

var jwt = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
jwt.SigningKey ??= Environment.GetEnvironmentVariable("LUMEN_JWT_KEY");

if (!jwt.IsConfigured)
{
    // Anybody holding this key can mint a token for any student, so there is no default. A
    // fallback would be identical on every deployment and published in this repository.
    if (!builder.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "Auth:Jwt:SigningKey (or LUMEN_JWT_KEY) must be set, and at least 32 characters. "
            + "There is deliberately no default: a shared signing key is every account on every "
            + "deployment.");
    }

    // Development gets a key that lasts as long as the process. Restarting signs everybody out,
    // which is mildly annoying and considerably better than a key in source control.
    jwt.SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
}

builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton<AccessTokens>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = jwt.Key(),
            ValidateLifetime = true,
            // Named explicitly rather than left to the defaults, because "which algorithms do
            // we accept" is the question behind every algorithm-confusion attack.
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            // The default five minutes of grace undoes a good part of a fifteen-minute token.
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

builder.Services.AddAuthorization();

// Cross-site cookies are for a split-origin development setup only. Never inferred from a
// hostname: guessing it wrong in production silently drops the CSRF protection SameSite=Lax
// gives for free.
var crossSiteCookies = builder.Configuration.GetValue("Auth:CrossSiteCookies", builder.Environment.IsDevelopment());
var cookiePolicy = new CookiePolicy(crossSiteCookies);

// The API describes itself, at /swagger, from a document at /openapi/v1.json.
//
// Off outside development unless somebody says otherwise, and never inferred from a hostname
// for the same reason the cookie switch above is not: the endpoints exist either way, but a
// browsable, try-it-now index of them is an invitation, and a deployment that wants one should
// have had to decide to. Turning it on is one setting; turning it on by accident should not be.
var exposeExplorer = builder.Configuration.GetValue("OpenApi:Expose", builder.Environment.IsDevelopment());

if (exposeExplorer)
{
    builder.Services.AddOpenApi(options =>
    {
        options.AddDocumentTransformer(ApiExplorer.DeclareBearerScheme);
        options.AddOperationTransformer(ApiExplorer.RequireBearerUnlessAnonymous);
    });
}

// A password check is slow on purpose, which protects a stolen database and does nothing about
// somebody trying ten thousand passwords against one live account.
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.AddPolicy(RateLimits.Auth, context => RateLimitPartition.GetFixedWindowLimiter(
        // By address, because the account being guessed at is not the attacker's to choose from.
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    // Enums travel as names. A client reading `"stage": 2` has to keep a copy of our numbering
    // in sync with ours, and it will drift.
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// The Angular workspace runs on :4200 in development. Deployed, both halves sit behind one
// origin and this does nothing.
const string DevelopmentCors = "lumen-dev";
builder.Services.AddCors(options => options.AddPolicy(DevelopmentCors, policy => policy
    .WithOrigins("http://localhost:4200", "http://127.0.0.1:4200")
    .AllowAnyHeader()
    .AllowAnyMethod()
    // The refresh token is a cookie, so the browser has to be told it may send it. Origins
    // stay an explicit list for exactly this reason — credentials and a wildcard origin
    // cannot mix. The access token needs none of this: it travels in a header the client sets.
    .AllowCredentials()));

var app = builder.Build();

if (app.Environment.IsDevelopment()) app.UseCors(DevelopmentCors);

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (exposeExplorer)
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "Lumen");
        options.RoutePrefix = "swagger";
        options.DocumentTitle = "Lumen API";
    });
}

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    // Said out loud, because "is this server charging people" is the first question anybody
    // debugging a deployment asks and the most expensive one to get wrong.
    payments = monnify.IsConfigured ? "monnify" : "none",
    // Said out loud because a development key means every token dies on the next restart, and
    // that is confusing to debug and trivial to explain.
    signingKey = builder.Configuration["Auth:Jwt:SigningKey"] is not null
                 || Environment.GetEnvironmentVariable("LUMEN_JWT_KEY") is not null
        ? "configured"
        : "ephemeral",
    billing = enforceBilling ? "enforced" : "open",
}))
.AllowAnonymous()
.WithTags(ApiTags.Service);

app.MapAuthEndpoints(cookiePolicy);
app.MapCourseEndpoints();
app.MapLessonEndpoints();
app.MapBillingEndpoints();
app.MapStudyEndpoints();

app.Run();

/// <summary>Exposed so the test host can reference this assembly.</summary>
public partial class Program;
