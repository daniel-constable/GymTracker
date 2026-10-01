using System.Net;
using GymTracker.Api;
using GymTracker.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GymTracker.Tests;

/// <summary>The progress API: which exercises have history, and the detail for one exercise.</summary>
public class ProgressEndpointTests : ApiTest
{
    private Task<List<ExerciseProgressSummaryDto>> ProgressList() => Get<List<ExerciseProgressSummaryDto>>("/api/progress");

    private async Task<ExerciseProgressDto> Progress(string exercise) =>
        await Get<ExerciseProgressDto>($"/api/progress/{(await Exercise(exercise)).Id}");

    /// <summary>Writes a finished workout straight into the database so it can be dated in the past.</summary>
    private async Task<int> SeedPastWorkout(int daysAgo, string exercise, params (double Weight, int Reps)[] sets)
    {
        using var scope = App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymDbContext>();
        var started = DateTime.UtcNow.AddDays(-daysAgo);
        var workout = new Workout
        {
            Name = "Push",
            StartedAt = started,
            FinishedAt = started.AddHours(1),
            Exercises =
            [
                new WorkoutExercise
                {
                    ExerciseId = (await db.Exercises.SingleAsync(e => e.Name == exercise)).Id,
                    Sets = sets.Select((s, i) => new SetEntry { Position = i, Weight = s.Weight, Reps = s.Reps }).ToList(),
                },
            ],
        };
        db.Workouts.Add(workout);
        await db.SaveChangesAsync();
        return workout.Id;
    }

    // ----- List -----

    [Fact]
    public async Task List_is_empty_before_anything_is_logged()
    {
        Assert.Empty(await ProgressList());
    }

    [Fact]
    public async Task List_has_one_entry_per_exercise_with_its_session_count()
    {
        await LogWorkout("Push", ("Bench Press", 60, 8), ("Bench Press", 60, 8), ("Dips", 0, 12));
        await LogWorkout("Push", ("Bench Press", 62.5, 8));

        var list = await ProgressList();

        Assert.Equal(2, list.Count);
        Assert.Equal(2, list.Single(p => p.Name == "Bench Press").SessionCount);
        Assert.Equal(1, list.Single(p => p.Name == "Dips").SessionCount);
        Assert.Equal((await Exercise("Dips")).Id, list.Single(p => p.Name == "Dips").ExerciseId);
    }

    [Fact]
    public async Task List_is_ordered_by_most_recently_trained()
    {
        await SeedPastWorkout(30, "Squat", (80, 5));
        await SeedPastWorkout(3, "Deadlift", (100, 5));
        await SeedPastWorkout(10, "Bench Press", (60, 5));
        var latest = await LogWorkout("Legs", ("Squat", 85, 5));

        var list = await ProgressList();

        Assert.Equal(["Squat", "Deadlift", "Bench Press"], list.Select(p => p.Name));
        Assert.Equal(latest.StartedAt, list[0].LastPerformed);
        Assert.Equal(DateTimeKind.Utc, list[0].LastPerformed.Kind);
    }

    [Fact]
    public async Task List_leaves_out_the_workout_in_progress_and_exercises_without_sets()
    {
        await LogWorkout("Push", ("Bench Press", 60, 8));
        var active = await StartWorkout("Pull");
        await LogSet((await AddExercise(active.Id, "Deadlift")).Id, 100, 5);
        await AddExercise(active.Id, "Barbell Row");

        Assert.Equal(["Bench Press"], (await ProgressList()).Select(p => p.Name));
    }

    [Fact]
    public async Task List_drops_an_exercise_when_its_only_workout_is_deleted()
    {
        var workout = await LogWorkout("Push", ("Bench Press", 60, 8));

        await Client.DeleteAsync($"/api/workouts/{workout.Id}");

        Assert.Empty(await ProgressList());
    }

    [Fact]
    public async Task List_still_includes_archived_exercises()
    {
        await LogWorkout("Push", ("Bench Press", 60, 8));
        await Client.DeleteAsync($"/api/exercises/{(await Exercise("Bench Press")).Id}"); // archives

        Assert.Equal(["Bench Press"], (await ProgressList()).Select(p => p.Name));
    }

    // ----- Detail -----

    [Fact]
    public async Task Detail_returns_404_for_an_unknown_exercise()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync("/api/progress/9999")).StatusCode);
    }

    [Fact]
    public async Task Detail_for_an_exercise_never_logged_is_empty()
    {
        var progress = await Progress("Bench Press");

        Assert.Equal("Bench Press", progress.Name);
        Assert.False(progress.IsArchived);
        Assert.Empty(progress.Sessions);
        Assert.Null(progress.Records);
        Assert.Empty(progress.Changes);
    }

    [Fact]
    public async Task Detail_lists_each_finished_session_oldest_first_with_its_sets_in_order()
    {
        var first = await LogWorkout("Push", ("Bench Press", 60, 8), ("Dips", 0, 12), ("Bench Press", 62.5, 6));
        var second = await LogWorkout("Pull", ("Bench Press", 65, 5));

        var progress = await Progress("Bench Press");

        Assert.Equal([first.Id, second.Id], progress.Sessions.Select(s => s.WorkoutId));
        Assert.Equal(["Push", "Pull"], progress.Sessions.Select(s => s.WorkoutName));
        Assert.Equal(first.StartedAt, progress.Sessions[0].Date);
        Assert.Equal([(60, 8), (62.5, 6)], progress.Sessions[0].Sets.Select(s => (s.Weight, s.Reps)));
        Assert.Equal(62.5, progress.Sessions[0].TopWeight);
        Assert.Equal(6, progress.Sessions[0].TopWeightReps);
        Assert.Equal(8, progress.Sessions[0].BestReps);
        Assert.Equal(855, progress.Sessions[0].Volume);
        Assert.Equal(76, progress.Sessions[0].EstimatedOneRepMax);   // 60 × 8
        Assert.Equal(75.8, progress.Sessions[1].EstimatedOneRepMax); // 65 × 5
    }

    [Fact]
    public async Task Detail_only_includes_the_requested_exercise()
    {
        await LogWorkout("Push", ("Bench Press", 60, 8), ("Dips", 0, 12));

        var dips = await Progress("Dips");

        Assert.Equal([(0, 12)], dips.Sessions.Single().Sets.Select(s => (s.Weight, s.Reps)));
        Assert.Null(dips.Records!.BestOneRepMax);
        Assert.Equal(12, dips.Records.MostReps.Value);
    }

    [Fact]
    public async Task Detail_excludes_the_workout_in_progress()
    {
        var finished = await LogWorkout("Push", ("Bench Press", 60, 8));
        var active = await StartWorkout("Push");
        await LogSet((await AddExercise(active.Id, "Bench Press")).Id, 200, 1);

        var progress = await Progress("Bench Press");

        Assert.Equal([finished.Id], progress.Sessions.Select(s => s.WorkoutId));
        Assert.Equal(60, progress.Records!.HeaviestSet.Value);
    }

    [Fact]
    public async Task Detail_counts_the_workout_once_it_is_finished()
    {
        await LogWorkout("Push", ("Bench Press", 60, 8));
        var active = await StartWorkout("Push");
        await LogSet((await AddExercise(active.Id, "Bench Press")).Id, 70, 5);
        await Finish(active.Id);

        var progress = await Progress("Bench Press");

        Assert.Equal(2, progress.Sessions.Count);
        Assert.Equal(70, progress.Records!.HeaviestSet.Value);
        Assert.Equal(active.Id, progress.Records.HeaviestSet.WorkoutId);
    }

    [Fact]
    public async Task Detail_reports_personal_records()
    {
        var heavyId = await SeedPastWorkout(60, "Bench Press", (100, 1), (80, 5));
        var repsId = await SeedPastWorkout(20, "Bench Press", (50, 20), (85, 8), (85, 8), (85, 8));

        var records = (await Progress("Bench Press")).Records!;

        Assert.Equal((100, 100, 1, heavyId), (records.HeaviestSet.Value, records.HeaviestSet.Weight, records.HeaviestSet.Reps, records.HeaviestSet.WorkoutId));
        Assert.Equal((107.7, 85, 8, repsId), (records.BestOneRepMax!.Value, records.BestOneRepMax.Weight, records.BestOneRepMax.Reps, records.BestOneRepMax.WorkoutId));
        Assert.Equal((20, 50, 20, repsId), (records.MostReps.Value, records.MostReps.Weight, records.MostReps.Reps, records.MostReps.WorkoutId));
        Assert.Equal((3040, repsId), (records.BestVolume.Value, records.BestVolume.WorkoutId));
        Assert.Null(records.BestVolume.Weight);
    }

    [Fact]
    public async Task Detail_shows_whether_more_weight_is_being_lifted_than_a_year_ago()
    {
        var yearAgoId = await SeedPastWorkout(400, "Bench Press", (60, 5));
        await SeedPastWorkout(200, "Bench Press", (70, 5));
        var quarterAgoId = await SeedPastWorkout(95, "Bench Press", (75, 5));
        var monthAgoId = await SeedPastWorkout(40, "Bench Press", (77.5, 5));
        await LogWorkout("Push", ("Bench Press", 80, 5));

        var changes = (await Progress("Bench Press")).Changes;

        Assert.Equal(["month", "threeMonths", "year"], changes.Select(c => c.Period));
        Assert.Equal([monthAgoId, quarterAgoId, yearAgoId], changes.Select(c => c.WorkoutId));
        var year = changes.Single(c => c.Period == "year");
        Assert.Equal(60, year.TopWeightThen);
        Assert.Equal(80, year.TopWeightNow);
        Assert.Equal(70, year.OneRepMaxThen);
        Assert.Equal(93.3, year.OneRepMaxNow);
        Assert.InRange(year.Date, DateTime.UtcNow.AddDays(-401), DateTime.UtcNow.AddDays(-399));
    }

    [Fact]
    public async Task Detail_compares_with_the_first_session_when_history_is_short()
    {
        var first = await LogWorkout("Push", ("Bench Press", 60, 8));
        await LogWorkout("Push", ("Bench Press", 62.5, 8));

        var change = (await Progress("Bench Press")).Changes.Single();

        Assert.Equal("start", change.Period);
        Assert.Equal(first.Id, change.WorkoutId);
        Assert.Equal(60, change.TopWeightThen);
        Assert.Equal(62.5, change.TopWeightNow);
    }

    [Fact]
    public async Task Detail_follows_corrections_and_deletions()
    {
        var first = await LogWorkout("Push", ("Bench Press", 60, 8));
        var second = await LogWorkout("Push", ("Bench Press", 100, 1));
        var typo = second.Exercises.Single().Sets.Single();

        await Client.PutAsync($"/api/sets/{typo.Id}", System.Net.Http.Json.JsonContent.Create(new { weight = 65, reps = 5 }));
        Assert.Equal(65, (await Progress("Bench Press")).Records!.HeaviestSet.Value);

        await Client.DeleteAsync($"/api/workouts/{second.Id}");
        var progress = await Progress("Bench Press");
        Assert.Equal([first.Id], progress.Sessions.Select(s => s.WorkoutId));
        Assert.Equal(60, progress.Records!.HeaviestSet.Value);
        Assert.Empty(progress.Changes);
    }

    [Fact]
    public async Task Detail_is_available_for_an_archived_exercise()
    {
        await LogWorkout("Push", ("Bench Press", 60, 8));
        var bench = await Exercise("Bench Press");
        await Client.DeleteAsync($"/api/exercises/{bench.Id}"); // archives

        var progress = await Get<ExerciseProgressDto>($"/api/progress/{bench.Id}");

        Assert.True(progress.IsArchived);
        Assert.Single(progress.Sessions);
    }

    [Fact]
    public async Task Detail_uses_the_exercises_current_name()
    {
        await LogWorkout("Push", ("Dips", 0, 12));
        var dips = await Exercise("Dips");
        await Client.PutAsync($"/api/exercises/{dips.Id}",
            System.Net.Http.Json.JsonContent.Create(new { name = "Weighted Dips", sessionTypeId = dips.SessionTypeId }));

        Assert.Equal("Weighted Dips", (await Get<ExerciseProgressDto>($"/api/progress/{dips.Id}")).Name);
        Assert.Equal(["Weighted Dips"], (await ProgressList()).Select(p => p.Name));
    }
}
