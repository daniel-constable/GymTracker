using GymTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace GymTracker.Api;

public static class ExerciseEndpoints
{
    public static void MapExerciseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/exercises");

        group.MapGet("/", async (GymDbContext db) =>
            await db.Exercises
                .OrderBy(e => e.Name)
                .Select(e => new ExerciseDto(e.Id, e.Name, e.SessionTypeId, e.IsArchived))
                .ToListAsync());

        group.MapPost("/", async (ExerciseRequest req, GymDbContext db) =>
        {
            if (await CheckAsync(req, null, db) is { } problem) return problem;

            var exercise = new Exercise { Name = req.Name!.Trim(), SessionTypeId = req.SessionTypeId };
            db.Exercises.Add(exercise);
            await db.SaveChangesAsync();
            return Results.Ok(ToDto(exercise));
        });

        group.MapPut("/{id:int}", async (int id, ExerciseRequest req, GymDbContext db) =>
        {
            var exercise = await db.Exercises.FindAsync(id);
            if (exercise is null) return Results.NotFound();
            if (await CheckAsync(req, id, db) is { } problem) return problem;

            exercise.Name = req.Name!.Trim();
            exercise.SessionTypeId = req.SessionTypeId;
            exercise.IsArchived = req.IsArchived;
            await db.SaveChangesAsync();
            return Results.Ok(ToDto(exercise));
        });

        // Exercises that appear in a workout are archived instead, so history stays intact.
        group.MapDelete("/{id:int}", async (int id, GymDbContext db) =>
        {
            var exercise = await db.Exercises.FindAsync(id);
            if (exercise is null) return Results.NotFound();

            var archived = await db.WorkoutExercises.AnyAsync(we => we.ExerciseId == id);
            if (archived)
                exercise.IsArchived = true;
            else
                db.Exercises.Remove(exercise);

            await db.SaveChangesAsync();
            return Results.Ok(new { archived });
        });
    }

    private static ExerciseDto ToDto(Exercise e) => new(e.Id, e.Name, e.SessionTypeId, e.IsArchived);

    private static async Task<IResult?> CheckAsync(ExerciseRequest req, int? existingId, GymDbContext db)
    {
        if (Validation.CheckName(req.Name, out var name) is { } error)
            return Validation.Problem("name", error);
        if (await db.Exercises.AnyAsync(e => e.Name == name && e.Id != existingId))
            return Validation.Problem("name", $"An exercise called \"{name}\" already exists.");
        if (req.SessionTypeId is { } sessionId && !await db.SessionTypes.AnyAsync(s => s.Id == sessionId))
            return Validation.Problem("sessionTypeId", "That session no longer exists.");
        return null;
    }
}
