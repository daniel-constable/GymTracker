using GymTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace GymTracker.Api;

public static class SessionTypeEndpoints
{
    public static void MapSessionTypeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/session-types");

        group.MapGet("/", async (GymDbContext db) =>
            await db.SessionTypes
                .OrderBy(s => s.SortOrder).ThenBy(s => s.Id)
                .Select(s => new SessionTypeDto(s.Id, s.Name))
                .ToListAsync());

        group.MapPost("/", async (NameRequest req, GymDbContext db) =>
        {
            if (Validation.CheckName(req.Name, out var name) is { } error)
                return Validation.Problem("name", error);
            if (await db.SessionTypes.AnyAsync(s => s.Name == name))
                return Validation.Problem("name", $"A session called \"{name}\" already exists.");

            var nextOrder = (await db.SessionTypes.MaxAsync(s => (int?)s.SortOrder) ?? -1) + 1;
            var session = new SessionType { Name = name, SortOrder = nextOrder };
            db.SessionTypes.Add(session);
            await db.SaveChangesAsync();
            return Results.Ok(new SessionTypeDto(session.Id, session.Name));
        });

        group.MapPut("/{id:int}", async (int id, NameRequest req, GymDbContext db) =>
        {
            var session = await db.SessionTypes.FindAsync(id);
            if (session is null) return Results.NotFound();

            if (Validation.CheckName(req.Name, out var name) is { } error)
                return Validation.Problem("name", error);
            if (await db.SessionTypes.AnyAsync(s => s.Name == name && s.Id != id))
                return Validation.Problem("name", $"A session called \"{name}\" already exists.");

            session.Name = name;
            await db.SaveChangesAsync();
            return Results.Ok(new SessionTypeDto(session.Id, session.Name));
        });

        // Exercises in the session become ungrouped; past workouts keep their own copy of the name.
        group.MapDelete("/{id:int}", async (int id, GymDbContext db) =>
        {
            var session = await db.SessionTypes.FindAsync(id);
            if (session is null) return Results.NotFound();

            if (await db.Workouts.AnyAsync(w => w.SessionTypeId == id && w.FinishedAt == null))
                return Validation.Problem("id", "Finish or discard the workout in progress before deleting this session.");

            db.SessionTypes.Remove(session);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }
}
