using GymTracker.Api;

namespace GymTracker.Tests;

/// <summary>The "last time" data shown against each exercise in a workout.</summary>
public class PreviousPerformanceTests : ApiTest
{
    private static IEnumerable<(double, int)> Values(PreviousPerformanceDto? previous) =>
        previous!.Sets.Select(s => (s.Weight, s.Reps));

    [Fact]
    public async Task There_is_no_previous_the_first_time_an_exercise_is_done()
    {
        var workout = await StartWorkout();

        var entry = await AddExercise(workout.Id, "Bench Press");

        Assert.Null(entry.Previous);
        Assert.Null((await GetWorkout(workout.Id)).Exercises.Single().Previous);
    }

    [Fact]
    public async Task Previous_shows_the_sets_date_and_workout_from_last_time()
    {
        var last = await LogWorkout("Push", ("Bench Press", 60, 8), ("Bench Press", 62.5, 6), ("Bench Press", 55, 10));
        var workout = await StartWorkout();

        var entry = await AddExercise(workout.Id, "Bench Press");

        Assert.NotNull(entry.Previous);
        Assert.Equal(last.Id, entry.Previous.WorkoutId);
        Assert.Equal(last.StartedAt, entry.Previous.Date);
        Assert.Equal([(60, 8), (62.5, 6), (55, 10)], Values(entry.Previous));
    }

    [Fact]
    public async Task Previous_is_the_same_when_the_workout_is_reloaded()
    {
        var last = await LogWorkout("Push", ("Bench Press", 60, 8));
        var workout = await StartWorkout();
        await AddExercise(workout.Id, "Bench Press");

        var reloaded = (await Get<WorkoutDto>("/api/workouts/active")).Exercises.Single();

        Assert.Equal(last.Id, reloaded.Previous!.WorkoutId);
        Assert.Equal([(60, 8)], Values(reloaded.Previous));
    }

    [Fact]
    public async Task Previous_uses_the_most_recent_workout_not_the_best_or_the_first()
    {
        await LogWorkout("Push", ("Bench Press", 100, 1));
        await LogWorkout("Push", ("Bench Press", 60, 8));
        var latest = await LogWorkout("Push", ("Bench Press", 50, 12));
        var workout = await StartWorkout();

        var entry = await AddExercise(workout.Id, "Bench Press");

        Assert.Equal(latest.Id, entry.Previous!.WorkoutId);
        Assert.Equal([(50, 12)], Values(entry.Previous));
    }

    [Fact]
    public async Task Previous_is_tracked_separately_for_each_exercise()
    {
        var older = await LogWorkout("Push", ("Bench Press", 60, 8), ("Dips", 0, 12));
        var newer = await LogWorkout("Push", ("Bench Press", 65, 6));
        var workout = await StartWorkout();

        var bench = await AddExercise(workout.Id, "Bench Press");
        var dips = await AddExercise(workout.Id, "Dips");
        var raise = await AddExercise(workout.Id, "Lateral Raise");

        Assert.Equal(newer.Id, bench.Previous!.WorkoutId);
        Assert.Equal([(65, 6)], Values(bench.Previous));
        Assert.Equal(older.Id, dips.Previous!.WorkoutId);
        Assert.Equal([(0, 12)], Values(dips.Previous));
        Assert.Null(raise.Previous);
    }

    [Fact]
    public async Task Previous_looks_across_session_types()
    {
        var pull = await LogWorkout("Pull", ("Bench Press", 40, 15));
        var workout = await StartWorkout("Push");

        var entry = await AddExercise(workout.Id, "Bench Press");

        Assert.Equal(pull.Id, entry.Previous!.WorkoutId);
    }

    [Fact]
    public async Task Previous_ignores_a_workout_where_the_exercise_was_added_but_not_done()
    {
        var done = await LogWorkout("Push", ("Bench Press", 60, 8));
        // Second workout: bench is added but only dips get logged, so bench is dropped on finish.
        var second = await StartWorkout();
        await AddExercise(second.Id, "Bench Press");
        var dips = await AddExercise(second.Id, "Dips");
        await LogSet(dips.Id, 0, 12);
        await Finish(second.Id);
        var workout = await StartWorkout();

        var entry = await AddExercise(workout.Id, "Bench Press");

        Assert.Equal(done.Id, entry.Previous!.WorkoutId);
    }

    [Fact]
    public async Task Previous_ignores_discarded_and_deleted_workouts()
    {
        var kept = await LogWorkout("Push", ("Bench Press", 60, 8));
        var deleted = await LogWorkout("Push", ("Bench Press", 70, 5));
        await Client.DeleteAsync($"/api/workouts/{deleted.Id}");
        var discarded = await StartWorkout();
        await LogSet((await AddExercise(discarded.Id, "Bench Press")).Id, 80, 3);
        await Client.DeleteAsync($"/api/workouts/{discarded.Id}");
        var workout = await StartWorkout();

        var entry = await AddExercise(workout.Id, "Bench Press");

        Assert.Equal(kept.Id, entry.Previous!.WorkoutId);
        Assert.Equal([(60, 8)], Values(entry.Previous));
    }

    [Fact]
    public async Task Previous_does_not_include_sets_from_the_current_workout()
    {
        var last = await LogWorkout("Push", ("Bench Press", 60, 8));
        var workout = await StartWorkout();
        var entry = await AddExercise(workout.Id, "Bench Press");
        await LogSet(entry.Id, 65, 8);
        await LogSet(entry.Id, 65, 7);

        var reloaded = (await GetWorkout(workout.Id)).Exercises.Single();

        Assert.Equal(last.Id, reloaded.Previous!.WorkoutId);
        Assert.Equal([(60, 8)], Values(reloaded.Previous));
        Assert.Equal(2, reloaded.Sets.Count);
    }

    [Fact]
    public async Task Previous_reflects_later_corrections_to_the_earlier_workout()
    {
        var last = await LogWorkout("Push", ("Bench Press", 60, 8), ("Bench Press", 60, 8));
        var sets = last.Exercises.Single().Sets;
        await Client.PutAsync($"/api/sets/{sets[0].Id}", System.Net.Http.Json.JsonContent.Create(new { weight = 61, reps = 9 }));
        await Client.DeleteAsync($"/api/sets/{sets[1].Id}");
        var workout = await StartWorkout();

        var entry = await AddExercise(workout.Id, "Bench Press");

        Assert.Equal([(61, 9)], Values(entry.Previous));
    }

    [Fact]
    public async Task In_history_previous_means_the_workout_before_that_one_not_the_latest()
    {
        var first = await LogWorkout("Push", ("Bench Press", 60, 8));
        var second = await LogWorkout("Push", ("Bench Press", 65, 8));
        var third = await LogWorkout("Push", ("Bench Press", 70, 8));

        Assert.Null((await GetWorkout(first.Id)).Exercises.Single().Previous);
        Assert.Equal(first.Id, (await GetWorkout(second.Id)).Exercises.Single().Previous!.WorkoutId);
        Assert.Equal(second.Id, (await GetWorkout(third.Id)).Exercises.Single().Previous!.WorkoutId);
    }

    [Fact]
    public async Task Previous_still_works_for_an_archived_exercise()
    {
        var last = await LogWorkout("Push", ("Bench Press", 60, 8));
        var bench = await Exercise("Bench Press");
        await Client.DeleteAsync($"/api/exercises/{bench.Id}"); // archives, because it has history
        var workout = await StartWorkout();

        var entry = await Read<WorkoutExerciseDto>(await PostWorkoutExercise(workout.Id, bench.Id));

        Assert.Equal(last.Id, entry.Previous!.WorkoutId);
    }
}
