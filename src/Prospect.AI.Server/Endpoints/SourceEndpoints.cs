using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Prospect.AI.Domain.Sources;
using Prospect.AI.Infrastructure.Persistence;

namespace Prospect.AI.Server.Endpoints;

/// <summary>
/// The signed-in user's sources. The owner query filter limits every query to that user.
/// </summary>
public static class SourceEndpoints
{
    public static IEndpointRouteBuilder MapSourceEndpoints(this IEndpointRouteBuilder routes)
    {
        var sources = routes.MapGroup("/sources")
            .RequireAuthorization()
            .WithTags("Sources");

        sources.MapGet("/", GetSources).WithName("GetSources");
        sources.MapPost("/", CreateSource).WithName("CreateSource");
        sources.MapPost("/{id:guid}/activate", ActivateSource).WithName("ActivateSource");
        sources.MapPost("/{id:guid}/deactivate", DeactivateSource).WithName("DeactivateSource");

        return routes;
    }

    private static async Task<Ok<List<SourceResponse>>> GetSources(
        ProspectDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var sources = await dbContext.Sources
            .OrderBy(source => source.Name)
            .Select(source => SourceResponse.From(source))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(sources);
    }

    private static async Task<Results<Created<SourceResponse>, ValidationProblem>> CreateSource(
        CreateSourceRequest request,
        ProspectDbContext dbContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var source = Source.Create(request.Name, request.Kind, request.Url, timeProvider.GetUtcNow());
        dbContext.Sources.Add(source);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/sources/{source.Id}", SourceResponse.From(source));
    }

    private static Task<Results<NoContent, NotFound>> ActivateSource(
        Guid id,
        ProspectDbContext dbContext,
        CancellationToken cancellationToken) =>
        UpdateSourceAsync(id, source => source.Activate(), dbContext, cancellationToken);

    private static Task<Results<NoContent, NotFound>> DeactivateSource(
        Guid id,
        ProspectDbContext dbContext,
        CancellationToken cancellationToken) =>
        UpdateSourceAsync(id, source => source.Deactivate(), dbContext, cancellationToken);

    private static async Task<Results<NoContent, NotFound>> UpdateSourceAsync(
        Guid id,
        Action<Source> update,
        ProspectDbContext dbContext,
        CancellationToken cancellationToken)
    {
        // Another user's source is filtered out, so it answers 404 like a missing one.
        var source = await dbContext.Sources.SingleOrDefaultAsync(source => source.Id == id, cancellationToken);
        if (source is null)
        {
            return TypedResults.NotFound();
        }

        update(source);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.NoContent();
    }

    private static Dictionary<string, string[]> Validate(CreateSourceRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > Source.NameMaxLength)
        {
            errors[nameof(request.Name)] = [$"Name is required and must not exceed {Source.NameMaxLength} characters."];
        }

        if (!Enum.IsDefined(request.Kind))
        {
            errors[nameof(request.Kind)] = ["Kind must be one of: " + string.Join(", ", Enum.GetNames<SourceKind>()) + "."];
        }

        var url = request.Url?.Trim();
        var urlRequired = request.Kind is SourceKind.Rss or SourceKind.Api;
        if (string.IsNullOrEmpty(url))
        {
            if (urlRequired)
            {
                errors[nameof(request.Url)] = [$"Url is required for {request.Kind} sources."];
            }
        }
        else if (url.Length > Source.UrlMaxLength
                 || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
                 || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            errors[nameof(request.Url)] = [$"Url must be an absolute http(s) URL of at most {Source.UrlMaxLength} characters."];
        }

        return errors;
    }

    public sealed record CreateSourceRequest(string Name, SourceKind Kind, string? Url);

    public sealed record SourceResponse(
        Guid Id,
        string Name,
        SourceKind Kind,
        string? Url,
        bool IsActive,
        DateTimeOffset CreatedAt,
        DateTimeOffset? LastCollectedAt)
    {
        public static SourceResponse From(Source source) =>
            new(source.Id, source.Name, source.Kind, source.Url, source.IsActive, source.CreatedAt, source.LastCollectedAt);
    }
}
