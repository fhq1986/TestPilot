using AI.TestPlatform.Api.Audit;
using AI.TestPlatform.Api.Auth;
using AI.TestPlatform.Api.Mocks;
using AI.TestPlatform.Application.Mocks;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.Modules.Mocks;

public static class MockApiExtensions
{
    public static RouteGroupBuilder MapMockApi(this RouteGroupBuilder group)
    {
        group.MapPost("/", async (
            CreateMockRequest request,
            IValidator<CreateMockRequest> validator,
            TestDbContext db,
            MockService mockService,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());

            var projectExists = await db.Projects.AnyAsync(p => p.Id == request.ProjectId, ct);
            if (!projectExists)
                return Results.BadRequest(new { message = "项目不存在" });

            MockDefinition definition;
            try
            {
                definition = await mockService.CreateAndStartAsync(
                    request.ProjectId, request.Name, request.Spec, ct);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }

            return Results.Created($"/api/mocks/{definition.Id}", ToDto(definition, mockService));
        }).WithPermission(Permission.ManageProjects).WithAudit("Create", "Mock");

        group.MapGet("/", async (
            Guid? projectId,
            TestDbContext db,
            MockService mockService,
            CancellationToken ct) =>
        {
            var query = db.MockDefinitions.AsNoTracking().AsQueryable();
            if (projectId.HasValue)
                query = query.Where(m => m.ProjectId == projectId.Value);
            var items = await query.OrderByDescending(m => m.CreatedAt).ToListAsync(ct);
            return Results.Ok(items.Select(m => ToDto(m, mockService)));
        }).WithPermission(Permission.ViewProjects).Produces<IEnumerable<MockDto>>();

        group.MapGet("/{id:guid}", async (
            Guid id,
            TestDbContext db,
            MockService mockService,
            CancellationToken ct) =>
        {
            var mock = await db.MockDefinitions.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == id, ct);
            return mock is null
                ? Results.NotFound()
                : Results.Ok(ToDto(mock, mockService));
        }).WithPermission(Permission.ViewProjects);

        group.MapPost("/{id:guid}/start", async (
            Guid id,
            TestDbContext db,
            MockService mockService,
            CancellationToken ct) =>
        {
            var mock = await db.MockDefinitions.FirstOrDefaultAsync(m => m.Id == id, ct);
            if (mock is null)
                return Results.NotFound();
            if (mock.Status == MockStatus.Running || mockService.IsRunning(id))
                return Results.BadRequest(new { message = "Mock 已在运行" });

            try
            {
                var port = await mockService.StartFromDefinitionAsync(mock, ct);
                mock.Status = MockStatus.Running;
                mock.Port = port;
                await db.SaveChangesAsync(ct);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }

            return Results.Ok(ToDto(mock, mockService));
        }).WithPermission(Permission.ManageProjects).WithAudit("Start", "Mock");

        group.MapDelete("/{id:guid}", async (
            Guid id,
            TestDbContext db,
            MockService mockService,
            CancellationToken ct) =>
        {
            var mock = await db.MockDefinitions.FirstOrDefaultAsync(m => m.Id == id, ct);
            if (mock is null)
                return Results.NotFound();

            await mockService.StopAsync(id, ct);
            db.MockDefinitions.Remove(mock);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).WithPermission(Permission.ManageProjects).WithAudit("Delete", "Mock");

        return group;
    }

    private static MockDto ToDto(MockDefinition mock, MockService service)
    {
        var running = mock.Status == MockStatus.Running && service.IsRunning(mock.Id);
        return new MockDto(
            mock.Id,
            mock.ProjectId,
            mock.Name,
            running ? mock.Port : null,
            running ? MockStatus.Running : MockStatus.Stopped,
            mock.CreatedAt);
    }
}
