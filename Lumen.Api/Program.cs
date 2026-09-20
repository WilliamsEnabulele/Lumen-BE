using System.Text.Json.Serialization;
using Anthropic;
using Lumen.Api.Endpoints;
using Lumen.Domain.Assessment;
using Lumen.Domain.Teaching;
using Lumen.Infrastructure.Ai;
using Lumen.Infrastructure.Billing;
using Lumen.Infrastructure.Extraction;
using Lumen.Infrastructure.Ingestion;
using Lumen.Infrastructure.Storage;

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
builder.Services.AddSingleton<IPaymentStore>(_ => new FilePaymentStore(dataRoot));
builder.Services.AddSingleton<IEntitlementStore>(_ => new FileEntitlementStore(dataRoot));
builder.Services.AddSingleton<IUploadLedger>(_ => new FileUploadLedger(dataRoot));
builder.Services.AddSingleton<ISessionStore>(_ => new FileSessionStore(dataRoot));
builder.Services.AddSingleton<IMasteryStore>(_ => new FileMasteryStore(dataRoot));
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
    .AllowAnyMethod()));

var app = builder.Build();

if (app.Environment.IsDevelopment()) app.UseCors(DevelopmentCors);

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    // Said out loud, because "is this server charging people" is the first question anybody
    // debugging a deployment asks and the most expensive one to get wrong.
    payments = monnify.IsConfigured ? "monnify" : "none",
    billing = enforceBilling ? "enforced" : "open",
}));
app.MapCourseEndpoints();
app.MapLessonEndpoints();
app.MapBillingEndpoints();

app.Run();

/// <summary>Exposed so the test host can reference this assembly.</summary>
public partial class Program;
