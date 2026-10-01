using GymTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace GymTracker.Api;

public static class WorkoutEndpoints
{
    public static void MapWorkoutEndpoints(this IEndpointRouteBuilder app)
    {
        var workouts = app.MapGroup("/api/workouts");

        // Finished workouts, newest first.
        workouts.MapGet("/", async (GymDbContext db, int skip = 0, int take = 30) =>
            await db.Workouts
                .Where(w => w.FinishedAt != null)
                .OrderByDescending(w => w.StartedAt)
                .Skip(Math.Max(skip, 0)).Take(Math.Clamp(take, 1, 100))
                .Select(w => new WorkoutSummaryDto(
                    w.Id, w.Name, w.StartedAt, w.FinishedAt,
                    w.Exercises.SelectMany(e => e.Sets).Count(),
                    w.Exercises.OrderBy(e => e.Position).Select(e => e.Exercise.Name).ToList()))
                .ToListAsync());

        // The workout in progress, or 204 if there isn't one.
        workouts.MapGet("/active", async (GymDbContext db) =>
        {
            var id = await db.Workouts.Where(w => w.FinishedAt == null).Select(w => (int?)w.Id).FirstOrDefaultAsync();
            return id is null ? Results.NoContent() : Results.Ok(await LoadWorkoutAsync(db, id.Value));
        });

        workouts.MapGet("/{id:int}", async (int id, GymDbContext db) =>
            await LoadWorkoutAsync(db, id) is { } workout ? Results.Ok(workout) : Results.NotFound());

        workouts.MapPost("/", async (StartWorkoutRequest req, GymDbContext db) =>
        {
            if (await db.Workouts.AnyAsync(w => w.FinishedAt == null))
                return Validation.Problem("workout", "A workout is already in progress. Finish or discard it first.");

            var session = req.SessionTypeId is { } sessionId ? await db.SessionTypes.FindAsync(sessionId) : null;
            if (session is null)
                return Validation.Problem("sessionTypeId", "Choose a session to start.");

            var workout = new Workout { Name = session.Name, SessionTypeId = session.Id, StartedAt = DateTime.UtcNow };
            db.Workouts.Add(workout);
            await db.SaveChangesAsync();
            return Results.Ok(await LoadWorkoutAsync(db, workout.Id));
        });

        workouts.MapPost("/{id:int}/finish", async (int id, GymDbContext db) =>
        {
            var workout = await db.Workouts
                .Include(w => w.Exercises).ThenInclude(e => e.Sets)
                .FirstOrDefaultAsync(w => w.Id == id);
            if (workout is null) return Results.NotFound();

            if (workout.FinishedAt is null)
            {
                if (!workout.Exercises.Any(e => e.Sets.Count > 0))
                    return Validation.Problem("workout", "Log at least one set before finishing, or discard the workout.");

                // Exercises that were added but never used are just clutter in the history.
                db.WorkoutExercises.RemoveRange(workout.Exercises.Where(e => e.Sets.Count == 0));
                workout.FinishedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }

            return Results.Ok(await LoadWorkoutAsync(db, id));
        });

        workouts.MapDelete("/{id:int}", async (int id, GymDbContext db) =>
        {
            var workout = await db.Workouts.FindAsync(id);
            if (workout is null) return Results.NotFound();

            db.Workouts.Remove(workout);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // What to offer when a workout starts: the exercises from the last finished workout of the same session,
        // in the order they were done, minus any that are archived or already added.
        workouts.MapGet("/{id:int}/suggested-exercises", async (int id, GymDbContext db) =>
        {
            var workout = await db.Workouts.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id);
            if (workout is null) return Results.NotFound();
            if (workout.SessionTypeId is null) return Results.Ok(new List<ExerciseDto>());

            var lastWorkoutId = await db.Workouts
                .Where(w => w.SessionTypeId == workout.SessionTypeId
                    && w.Id != id
                    && w.FinishedAt != null
                    && w.StartedAt < workout.StartedAt)
                .OrderByDescending(w => w.StartedAt)
                .Select(w => (int?)w.Id)
                .FirstOrDefaultAsync();

            var suggestions = await db.WorkoutExercises.AsNoTracking()
                .Where(we => we.WorkoutId == lastWorkoutId
                    && !we.Exercise.IsArchived
                    && !db.WorkoutExercises.Any(current => current.WorkoutId == id && current.ExerciseId == we.ExerciseId))
                .OrderBy(we => we.Position)
                .Select(we => new ExerciseDto(we.Exercise.Id, we.Exercise.Name, we.Exercise.SessionTypeId, we.Exercise.IsArchived))
                .ToListAsync();
            return Results.Ok(suggestions);
        });

        workouts.MapPost("/{id:int}/exercises", async (int id, AddWorkoutExerciseRequest req, GymDbContext db) =>
        {
            var workout = await db.Workouts.FindAsync(id);
            if (workout is null) return Results.NotFound();

            var exercise = req.ExerciseId is { } exerciseId ? await db.Exercises.FindAsync(exerciseId) : null;
            if (exercise is null)
                return Validation.Problem("exerciseId", "Choose an exercise to add.");
            if (await db.WorkoutExercises.AnyAsync(we => we.WorkoutId == id && we.ExerciseId == exercise.Id))
                return Validation.Problem("exerciseId", $"{exercise.Name} is already in this workout.");

            var position = (await db.WorkoutExercises.Where(we => we.WorkoutId == id).MaxAsync(we => (int?)we.Position) ?? -1) + 1;
            var entry = new WorkoutExercise { WorkoutId = id, ExerciseId = exercise.Id, Position = position };
            db.WorkoutExercises.Add(entry);
            await db.SaveChangesAsync();

            var previous = await FindPreviousAsync(db, exercise.Id, workout);
            return Results.Ok(new WorkoutExerciseDto(entry.Id, exercise.Id, exercise.Name, [], previous));
        });

        var workoutExercises = app.MapGroup("/api/workout-exercises");

        workoutExercises.MapDelete("/{id:int}", async (int id, GymDbContext db) =>
        {
            var entry = await db.WorkoutExercises.FindAsync(id);
            if (entry is null) return Results.NotFound();

            db.WorkoutExercises.Remove(entry);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        workoutExercises.MapPost("/{id:int}/sets", async (int id, SetRequest req, GymDbContext db) =>
        {
            if (!await db.WorkoutExercises.AnyAsync(we => we.Id == id)) return Results.NotFound();
            if (Validation.CheckSet(req) is { } problem) return problem;

            var position = (await db.Sets.Where(s => s.WorkoutExerciseId == id).MaxAsync(s => (int?)s.Position) ?? -1) + 1;
            var set = new SetEntry { WorkoutExerciseId = id, Position = position, Weight = req.Weight!.Value, Reps = req.Reps!.Value };
            db.Sets.Add(set);
            await db.SaveChangesAsync();
            return Results.Ok(new SetDto(set.Id, set.Weight, set.Reps));
        });

        var sets = app.MapGroup("/api/sets");

        sets.MapPut("/{id:int}", async (int id, SetRequest req, GymDbContext db) =>
        {
            var set = await db.Sets.FindAsync(id);
            if (set is null) return Results.NotFound();
            if (Validation.CheckSet(req) is { } problem) return problem;

            set.Weight = req.Weight!.Value;
            set.Reps = req.Reps!.Value;
            await db.SaveChangesAsync();
            return Results.Ok(new SetDto(set.Id, set.Weight, set.Reps));
        });

        sets.MapDelete("/{id:int}", async (int id, GymDbContext db) =>
        {
            var set = await db.Sets.FindAsync(id);
            if (set is null) return Results.NotFound();

            db.Sets.Remove(set);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    private static async Task<WorkoutDto?> LoadWorkoutAsync(GymDbContext db, int id)
    {
        var workout = await db.Workouts.AsNoTracking()
            .Include(w => w.Exercises).ThenInclude(e => e.Exercise)
            .Include(w => w.Exercises).ThenInclude(e => e.Sets)
            .AsSplitQuery()
            .FirstOrDefaultAsync(w => w.Id == id);
        if (workout is null) return null;

        var exercises = new List<WorkoutExerciseDto>();
        foreach (var entry in workout.Exercises.OrderBy(e => e.Position))
        {
            exercises.Add(new WorkoutExerciseDto(
                entry.Id, entry.ExerciseId, entry.Exercise.Name,
                entry.Sets.OrderBy(s => s.Position).Select(s => new SetDto(s.Id, s.Weight, s.Reps)).ToList(),
                await FindPreviousAsync(db, entry.ExerciseId, workout)));
        }

        return new WorkoutDto(workout.Id, workout.Name, workout.SessionTypeId, workout.StartedAt, workout.FinishedAt, exercises);
    }

    /// <summary>What was logged for this exercise in the most recent finished workout before the given one.</summary>
    private static async Task<PreviousPerformanceDto?> FindPreviousAsync(GymDbContext db, int exerciseId, Workout workout)
    {
        var previous = await db.WorkoutExercises.AsNoTracking()
            .Where(we => we.ExerciseId == exerciseId
                && we.WorkoutId != workout.Id
                && we.Workout.FinishedAt != null
                && we.Workout.StartedAt < workout.StartedAt
                && we.Sets.Any())
            .OrderByDescending(we => we.Workout.StartedAt)
            .Select(we => new { we.Id, we.WorkoutId, we.Workout.StartedAt })
            .FirstOrDefaultAsync();
        if (previous is null) return null;

        var sets = await db.Sets.AsNoTracking()
            .Where(s => s.WorkoutExerciseId == previous.Id)
            .OrderBy(s => s.Position)
            .Select(s => new SetDto(s.Id, s.Weight, s.Reps))
            .ToListAsync();
        return new PreviousPerformanceDto(previous.WorkoutId, previous.StartedAt, sets);
    }
}
