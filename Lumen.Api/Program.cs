using System.Text.Json.Serialization;
using Anthropic;
using Lumen.Api.Endpoints;
using Lumen.Domain.Teaching;
using Lumen.Infrastructure.Ai;
using Lumen.Infrastructure.Extraction;
using Lumen.Infrastructure.Ingestion;
using Lumen.Infrastructure.Storage;

var builder = WebApplication.CreateBuilder(args);

var dataRoot = builder.Configuration["Storage:Root"]
               ?? Path.Combine(builder.Environment.ContentRootPath, ".local-storage");

builder.Services.AddSingleton<IDocumentExtractor, PlainTextExtractor>();
builder.Services.AddSingleton<IDocumentExtractor, WordExtractor>();
builder.Services.AddSingleton<IDocumentExtractor, SlidesExtractor>();
builder.Services.AddSingleton<DocumentExtractors>();
builder.Services.AddSingleton<ICourseStore>(_ => new FileCourseStore(dataRoot));
builder.Services.AddSingleton<IUploadStorage>(_ => new LocalDiskUploadStorage(dataRoot));
builder.Services.AddSingleton<ISessionStore, InMemorySessionStore>();
builder.Services.AddSingleton<IngestionPipeline>();

// The AI layer. Reading the document and teaching from it are both model work; the
// deterministic pair behind them is a degraded mode that keeps the upload path runnable
// without a key, not a second implementation of the product.
var ai = builder.Configuration.GetSection(AnthropicOptions.Section).Get<AnthropicOptions>() ?? new AnthropicOptions();
ai.ApiKey ??= builder.Configuration["Ai:ApiKey"] ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
builder.Services.AddSingleton(ai);

if (ai.IsConfigured)
{
    builder.Services.AddSingleton(_ => new AnthropicClient { ApiKey = ai.ApiKey });
    builder.Services.AddSingleton<ILessonAuthor, AnthropicLessonAuthor>();
    builder.Services.AddSingleton<ITutorBrain, AnthropicTutorBrain>();
}
else
{
    builder.Services.AddSingleton<ILessonAuthor, DeterministicLessonAuthor>();
    builder.Services.AddSingleton<ITutorBrain, ScriptedTutorBrain>();
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

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapCourseEndpoints();
app.MapLessonEndpoints();

app.Run();

/// <summary>Exposed so the test host can reference this assembly.</summary>
public partial class Program;
