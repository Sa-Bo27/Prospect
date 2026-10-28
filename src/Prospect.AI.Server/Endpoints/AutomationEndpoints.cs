using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Prospect.AI.Domain.Sources;
using Prospect.AI.Infrastructure.Ingestion;
using Prospect.AI.Infrastructure.Persistence;
using Prospect.AI.Server.Authentication;

namespace Prospect.AI.Server.Endpoints;

/// <summary>
/// Endpoints called by n8n workflows, authenticated with the shared API key (X-Api-Key header).
/// There is no signed-in user here: queries opt out of the owner filter explicitly.
/// </summary>
public static class AutomationEndpoints
{
    public static IEndpointRouteBuilder MapAutomationEndpoints(this IEndpointRouteBuilder routes)
    {
        var automation = routes.MapGroup("/automation")
            .RequireAuthorization(AuthenticationExtensions.AutomationPolicy)
            .WithTags("Automation");

        // Lets an n8n workflow check its URL and API key before doing real work.
        automation.MapGet("/ping", () => TypedResults.Ok(new { Status = "ok" }))
            .WithName("AutomationPing");

        automation.MapGet("/sources", GetActiveSources)
            .WithName("GetActiveSources");

        automation.MapPost("/ingest", IngestPostings)
            .WithName("IngestPostings");

        return routes;
    }

    /// <summary>Active sources of every user, so workflow A knows which feeds to read.</summary>
    private static async Task<Ok<List<AutomationSourceResponse>>> GetActiveSources(
        ProspectDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var sources = await dbContext.Sources
            .IgnoreQueryFilters()
            .Where(source => source.IsActive)
            .OrderBy(source => source.Id)
            .Select(source => new AutomationSourceResponse(source.Id, source.Name, source.Kind, source.Url))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(sources);
    }

    private static async Task<Results<Ok<IngestionSummary>, ProblemHttpResult, ValidationProblem>> IngestPostings(
        IngestRequest request,
        PostingIngestionService ingestionService,
        CancellationToken cancellationToken)
    {
        var postings = request.Postings ?? [];
        if (postings.Count > PostingIngestionService.MaxBatchSize)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Postings)] = [$"A batch must not exceed {PostingIngestionService.MaxBatchSize} postings."],
            });
        }

        var result = await ingestionService.IngestAsync(request.SourceId, postings, cancellationToken);

        return result.Status switch
        {
            IngestionStatus.Completed => TypedResults.Ok(result.Summary!),
            IngestionStatus.SourceNotFound => TypedResults.Problem(
                $"Source '{request.SourceId}' does not exist.", statusCode: StatusCodes.Status404NotFound),
            _ => TypedResults.Problem(
                $"Source '{request.SourceId}' is inactive.", statusCode: StatusCodes.Status409Conflict),
        };
    }

    public sealed record IngestRequest(Guid SourceId, List<IncomingPosting>? Postings);

    public sealed record AutomationSourceResponse(Guid Id, string Name, SourceKind Kind, string? Url);
}
