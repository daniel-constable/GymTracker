using GymTracker.Api;
using Microsoft.Extensions.DependencyInjection;

namespace GymTracker.Tests;

/// <summary>The list of finished workouts.</summary>
public class HistoryTests : ApiTest
{
    [Fact]
    public async Task History_is_empty_to_begin_with()
    {
        Assert.Empty(await History());
    }

    [Fact]
    public async Task History_does_not_include_the_workout_in_progress()
    {
        var finished = await LogWorkout("Push", ("Bench Press", 60, 8));
        var active = await StartWorkout("Pull");
        await LogSet((await AddExercise(active.Id, "Deadlift")).Id, 100, 5);

        var history = await History();

        Assert.Equal([finished.Id], history.Select(w => w.Id));
    }

    [Fact]
    public async Task History_lists_newest_first()
    {
        var push = await LogWorkout("Push", ("Bench Press", 60, 8));
        var pull = await LogWorkout("Pull", ("Deadlift", 100, 5));
        var legs = await LogWorkout("Legs", ("Squat", 80, 5));

        var history = await History();

        Assert.Equal([legs.Id, pull.Id, push.Id], history.Select(w => w.Id));
        Assert.Equal(["Legs", "Pull", "Push"], history.Select(w => w.Name));
    }

    [Fact]
    public async Task Summary_has_times_set_count_and_exercise_names_in_workout_order()
    {
        var workout = await LogWorkout("Push",
            ("Overhead Press", 40, 8),
            ("Bench Press", 60, 8),
            ("Overhead Press", 40, 7),
            ("Dips", 0, 12),
            ("Bench Press", 60, 6));

        var summary = (await History()).Single();

        Assert.Equal(workout.Id, summary.Id);
        Assert.Equal("Push", summary.Name);
        Assert.Equal(workout.StartedAt, summary.StartedAt);
        Assert.Equal(workout.FinishedAt, summary.FinishedAt);
        Assert.Equal(DateTimeKind.Utc, summary.StartedAt.Kind);
        Assert.Equal(5, summary.SetCount);
        Assert.Equal(["Overhead Press", "Bench Press", "Dips"], summary.ExerciseNames);
    }

    [Fact]
    public async Task Summary_counts_follow_later_edits()
    {
        var workout = await LogWorkout("Push", ("Bench Press", 60, 8), ("Bench Press", 60, 8), ("Dips", 0, 12));
        var dips = workout.Exercises.Single(e => e.ExerciseName == "Dips");

        await Client.DeleteAsync($"/api/workout-exercises/{dips.Id}");

        var summary = (await History()).Single();
        Assert.Equal(2, summary.SetCount);
        Assert.Equal(["Bench Press"], summary.ExerciseNames);
    }

    [Fact]
    public async Task History_detail_matches_what_was_logged()
    {
        var workout = await LogWorkout("Legs", ("Squat", 80, 5), ("Squat", 85, 5), ("Leg Press", 150, 10));

        var detail = await GetWorkout(workout.Id);

        Assert.Equal("Legs", detail.Name);
        Assert.Equal(["Squat", "Leg Press"], detail.Exercises.Select(e => e.ExerciseName));
        Assert.Equal([(80, 5), (85, 5)], detail.Exercises[0].Sets.Select(s => (s.Weight, s.Reps)));
        Assert.Equal([(150, 10)], detail.Exercises[1].Sets.Select(s => (s.Weight, s.Reps)));
    }

    [Fact]
    public async Task History_can_be_paged_with_skip_and_take()
    {
        var ids = new List<int>();
        for (var i = 0; i < 5; i++)
            ids.Add((await LogWorkout("Push", ("Bench Press", 60 + i, 8))).Id);
        ids.Reverse(); // newest first

        Assert.Equal(ids[..2], (await History("?skip=0&take=2")).Select(w => w.Id));
        Assert.Equal(ids[2..4], (await History("?skip=2&take=2")).Select(w => w.Id));
        Assert.Equal(ids[4..], (await History("?skip=4&take=2")).Select(w => w.Id));
        Assert.Empty(await History("?skip=5&take=2"));
        Assert.Equal(ids[..3], (await History("?take=3")).Select(w => w.Id));
        Assert.Equal(ids[3..], (await History("?skip=3")).Select(w => w.Id));
    }

    [Fact]
    public async Task History_tolerates_out_of_range_paging_values()
    {
        var first = await LogWorkout("Push", ("Bench Press", 60, 8));
        var second = await LogWorkout("Push", ("Bench Press", 65, 8));

        Assert.Equal([second.Id, first.Id], (await History("?skip=-5")).Select(w => w.Id));
        Assert.Equal([second.Id], (await History("?take=0")).Select(w => w.Id));
        Assert.Equal([second.Id], (await History("?take=-1")).Select(w => w.Id));
        Assert.Equal(2, (await History("?take=100000")).Count);
    }

    [Fact]
    public async Task History_returns_30_by_default_and_never_more_than_100()
    {
        // Seed workouts straight into the database; going through the API for 105 would be slow.
        using (var scope = App.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GymTracker.Data.GymDbContext>();
            var start = DateTime.UtcNow.AddDays(-200);
            for (var i = 0; i < 105; i++)
            {
                db.Workouts.Add(new GymTracker.Data.Workout
                {
                    Name = $"Workout {i}",
                    StartedAt = start.AddDays(i),
                    FinishedAt = start.AddDays(i).AddHours(1),
                });
            }
            await db.SaveChangesAsync();
        }

        var byDefault = await History();
        var capped = await History("?take=500");

        Assert.Equal(30, byDefault.Count);
        Assert.Equal("Workout 104", byDefault[0].Name);
        Assert.Equal("Workout 75", byDefault[^1].Name);
        Assert.Equal(100, capped.Count);
        Assert.Equal("Workout 5", capped[^1].Name);
    }
}
