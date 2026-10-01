using System.Net;
using GymTracker.Api;

namespace GymTracker.Tests;

/// <summary>The "same as last time?" exercise suggestions offered when a workout starts.</summary>
public class WorkoutSuggestionTests : ApiTest
{
    private async Task<List<string>> Suggestions(int workoutId) =>
        (await Get<List<ExerciseDto>>($"/api/workouts/{workoutId}/suggested-exercises")).Select(e => e.Name).ToList();

    [Fact]
    public async Task There_are_no_suggestions_for_the_first_workout_of_a_session()
    {
        var workout = await StartWorkout("Push");

        Assert.Empty(await Suggestions(workout.Id));
    }

    [Fact]
    public async Task Suggestions_are_the_exercises_from_the_last_workout_of_the_same_session_in_order()
    {
        await LogWorkout("Push", ("Overhead Press", 40, 8), ("Bench Press", 60, 8), ("Overhead Press", 40, 6), ("Dips", 0, 12));
        var workout = await StartWorkout("Push");

        var suggestions = await Get<List<ExerciseDto>>($"/api/workouts/{workout.Id}/suggested-exercises");

        Assert.Equal(["Overhead Press", "Bench Press", "Dips"], suggestions.Select(e => e.Name));
        Assert.Equal(await Exercise("Bench Press"), suggestions[1]);
    }

    [Fact]
    public async Task Suggestions_come_from_the_most_recent_workout_only()
    {
        await LogWorkout("Push", ("Bench Press", 60, 8), ("Dips", 0, 12));
        await LogWorkout("Push", ("Overhead Press", 40, 8), ("Lateral Raise", 8, 15));
        var workout = await StartWorkout("Push");

        Assert.Equal(["Overhead Press", "Lateral Raise"], await Suggestions(workout.Id));
    }

    [Fact]
    public async Task Suggestions_ignore_workouts_of_other_sessions()
    {
        await LogWorkout("Push", ("Bench Press", 60, 8));
        await LogWorkout("Pull", ("Deadlift", 100, 5));
        await LogWorkout("Legs", ("Squat", 80, 5));

        var push = await StartWorkout("Push");
        Assert.Equal(["Bench Press"], await Suggestions(push.Id));
        await Client.DeleteAsync($"/api/workouts/{push.Id}");

        var core = await StartWorkout("Core");
        Assert.Empty(await Suggestions(core.Id));
    }

    [Fact]
    public async Task Suggestions_follow_the_session_not_the_exercises_usual_group()
    {
        await LogWorkout("Push", ("Squat", 80, 5), ("Bench Press", 60, 8));
        var workout = await StartWorkout("Push");

        Assert.Equal(["Squat", "Bench Press"], await Suggestions(workout.Id));
    }

    [Fact]
    public async Task Suggestions_leave_out_exercises_that_were_added_but_not_done()
    {
        var last = await StartWorkout("Push");
        await AddExercise(last.Id, "Bench Press");
        await LogSet((await AddExercise(last.Id, "Dips")).Id, 0, 12);
        await Finish(last.Id); // bench had no sets, so it was dropped
        var workout = await StartWorkout("Push");

        Assert.Equal(["Dips"], await Suggestions(workout.Id));
    }

    [Fact]
    public async Task Suggestions_leave_out_exercises_already_in_the_workout()
    {
        await LogWorkout("Push", ("Bench Press", 60, 8), ("Overhead Press", 40, 8), ("Dips", 0, 12));
        var workout = await StartWorkout("Push");
        await AddExercise(workout.Id, "Overhead Press");

        Assert.Equal(["Bench Press", "Dips"], await Suggestions(workout.Id));
    }

    [Fact]
    public async Task Suggestions_leave_out_archived_exercises()
    {
        await LogWorkout("Push", ("Bench Press", 60, 8), ("Dips", 0, 12));
        await Client.DeleteAsync($"/api/exercises/{(await Exercise("Dips")).Id}"); // archives
        var workout = await StartWorkout("Push");

        Assert.Equal(["Bench Press"], await Suggestions(workout.Id));
    }

    [Fact]
    public async Task Suggestions_ignore_discarded_and_deleted_workouts()
    {
        await LogWorkout("Push", ("Bench Press", 60, 8));
        var deleted = await LogWorkout("Push", ("Dips", 0, 12));
        await Client.DeleteAsync($"/api/workouts/{deleted.Id}");
        var discarded = await StartWorkout("Push");
        await LogSet((await AddExercise(discarded.Id, "Lateral Raise")).Id, 8, 15);
        await Client.DeleteAsync($"/api/workouts/{discarded.Id}");
        var workout = await StartWorkout("Push");

        Assert.Equal(["Bench Press"], await Suggestions(workout.Id));
    }

    [Fact]
    public async Task Suggestions_for_a_finished_workout_look_at_the_one_before_it()
    {
        var first = await LogWorkout("Push", ("Bench Press", 60, 8));
        var second = await LogWorkout("Push", ("Dips", 0, 12));
        await LogWorkout("Push", ("Lateral Raise", 8, 15));

        Assert.Empty(await Suggestions(first.Id));
        Assert.Equal(["Bench Press"], await Suggestions(second.Id));
    }

    [Fact]
    public async Task Suggestions_are_empty_once_the_session_has_been_deleted()
    {
        await LogWorkout("Core", ("Plank", 0, 60));
        var workout = await LogWorkout("Core", ("Plank", 0, 70));
        await Client.DeleteAsync($"/api/session-types/{await SessionId("Core")}");

        Assert.Empty(await Suggestions(workout.Id));
    }

    [Fact]
    public async Task Suggestions_return_404_for_an_unknown_workout()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync("/api/workouts/9999/suggested-exercises")).StatusCode);
    }
}
