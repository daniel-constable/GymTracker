using GymTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace GymTracker.Api;

public static class ProgressEndpoints
{
    public static void MapProgressEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/progress");

        // Exercises that have been logged in a finished workout, most recently trained first.
        group.MapGet("/", async (GymDbContext db) =>
        {
            var logged = await db.WorkoutExercises.AsNoTracking()
                .Where(we => we.Workout.FinishedAt != null && we.Sets.Any())
                .Select(we => new { we.ExerciseId, we.Exercise.Name, we.Workout.StartedAt })
                .ToListAsync();

            return logged
                .GroupBy(x => (x.ExerciseId, x.Name))
                .Select(g => new ExerciseProgressSummaryDto(g.Key.ExerciseId, g.Key.Name, g.Count(), g.Max(x => x.StartedAt)))
                .OrderByDescending(s => s.LastPerformed)
                .ThenBy(s => s.Name)
                .ToList();
        });

        group.MapGet("/{exerciseId:int}", async (int exerciseId, GymDbContext db, TimeProvider clock) =>
        {
            var exercise = await db.Exercises.AsNoTracking().FirstOrDefaultAsync(e => e.Id == exerciseId);
            if (exercise is null) return Results.NotFound();

            var entries = await db.WorkoutExercises.AsNoTracking()
                .Where(we => we.ExerciseId == exerciseId && we.Workout.FinishedAt != null && we.Sets.Any())
                .Select(we => new
                {
                    we.WorkoutId,
                    we.Workout.Name,
                    we.Workout.StartedAt,
                    Sets = we.Sets.OrderBy(s => s.Position).Select(s => new SetDto(s.Id, s.Weight, s.Reps)).ToList(),
                })
                .ToListAsync();

            var inputs = entries.Select(e => new ProgressCalculator.SessionInput(e.WorkoutId, e.Name, e.StartedAt, e.Sets));
            return Results.Ok(ProgressCalculator.Build(
                exercise.Id, exercise.Name, exercise.IsArchived, inputs, clock.GetUtcNow().UtcDateTime));
        });
    }
}
