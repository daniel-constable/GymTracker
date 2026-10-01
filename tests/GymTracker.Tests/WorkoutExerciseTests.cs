using System.Net;
using System.Net.Http.Json;
using GymTracker.Api;

namespace GymTracker.Tests;

/// <summary>Adding exercises to a workout and removing them again.</summary>
public class WorkoutExerciseTests : ApiTest
{
    [Fact]
    public async Task Add_returns_the_new_entry_with_no_sets()
    {
        var workout = await StartWorkout();
        var bench = await Exercise("Bench Press");

        var entry = await Read<WorkoutExerciseDto>(await PostWorkoutExercise(workout.Id, bench.Id));

        Assert.Equal(bench.Id, entry.ExerciseId);
        Assert.Equal("Bench Press", entry.ExerciseName);
        Assert.Empty(entry.Sets);
        Assert.Null(entry.Previous);
    }

    [Fact]
    public async Task Exercises_are_kept_in_the_order_they_were_added()
    {
        var workout = await StartWorkout();
        string[] order = ["Tricep Pushdown", "Bench Press", "Lateral Raise", "Dips"];
        foreach (var name in order)
            await AddExercise(workout.Id, name);

        Assert.Equal(order, (await GetWorkout(workout.Id)).Exercises.Select(e => e.ExerciseName));
    }

    [Fact]
    public async Task Exercises_from_any_session_can_be_added()
    {
        var workout = await StartWorkout("Push");

        await AddExercise(workout.Id, "Squat");
        await AddExercise(workout.Id, "Plank");

        Assert.Equal(["Squat", "Plank"], (await GetWorkout(workout.Id)).Exercises.Select(e => e.ExerciseName));
    }

    [Fact]
    public async Task Add_rejects_an_exercise_that_is_already_in_the_workout()
    {
        var workout = await StartWorkout();
        await AddExercise(workout.Id, "Bench Press");

        var message = await AssertValidationError(
            await PostWorkoutExercise(workout.Id, (await Exercise("Bench Press")).Id), "exerciseId");

        Assert.Contains("already in this workout", message);
        Assert.Single((await GetWorkout(workout.Id)).Exercises);
    }

    [Fact]
    public async Task Add_rejects_a_missing_exercise_id()
    {
        var workout = await StartWorkout();

        await AssertValidationError(await PostWorkoutExercise(workout.Id, null), "exerciseId");
    }

    [Fact]
    public async Task Add_rejects_an_unknown_exercise()
    {
        var workout = await StartWorkout();

        await AssertValidationError(await PostWorkoutExercise(workout.Id, 9999), "exerciseId");

        Assert.Empty((await GetWorkout(workout.Id)).Exercises);
    }

    [Fact]
    public async Task Add_returns_404_for_an_unknown_workout()
    {
        var response = await PostWorkoutExercise(9999, (await Exercise("Bench Press")).Id);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_newly_created_exercise_can_be_added_straight_away()
    {
        var workout = await StartWorkout("Push");
        var created = await Read<ExerciseDto>(await Client.PostAsJsonAsync("/api/exercises",
            new { name = "Cable Fly", sessionTypeId = workout.SessionTypeId }));

        var entry = await Read<WorkoutExerciseDto>(await PostWorkoutExercise(workout.Id, created.Id));

        Assert.Equal("Cable Fly", entry.ExerciseName);
    }

    [Fact]
    public async Task Renaming_an_exercise_is_reflected_in_workouts_that_use_it()
    {
        var workout = await LogWorkout("Push", ("Dips", 0, 12));
        var dips = await Exercise("Dips");

        await Client.PutAsJsonAsync($"/api/exercises/{dips.Id}", new { name = "Weighted Dips", sessionTypeId = dips.SessionTypeId });

        Assert.Equal("Weighted Dips", (await GetWorkout(workout.Id)).Exercises.Single().ExerciseName);
        Assert.Equal(["Weighted Dips"], (await History()).Single().ExerciseNames);
    }

    [Fact]
    public async Task Remove_takes_the_exercise_and_its_sets_out_of_the_workout()
    {
        var workout = await StartWorkout();
        var bench = await AddExercise(workout.Id, "Bench Press");
        var dips = await AddExercise(workout.Id, "Dips");
        var benchSet = await LogSet(bench.Id, 60, 8);
        await LogSet(dips.Id, 0, 12);

        var response = await Client.DeleteAsync($"/api/workout-exercises/{bench.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var remaining = (await GetWorkout(workout.Id)).Exercises.Single();
        Assert.Equal("Dips", remaining.ExerciseName);
        Assert.Single(remaining.Sets);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.DeleteAsync($"/api/sets/{benchSet.Id}")).StatusCode);
    }

    [Fact]
    public async Task Remove_returns_404_for_an_unknown_entry()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Client.DeleteAsync("/api/workout-exercises/9999")).StatusCode);
    }

    [Fact]
    public async Task A_removed_exercise_can_be_added_again_and_goes_to_the_end()
    {
        var workout = await StartWorkout();
        var bench = await AddExercise(workout.Id, "Bench Press");
        await AddExercise(workout.Id, "Dips");
        await Client.DeleteAsync($"/api/workout-exercises/{bench.Id}");

        var readded = await AddExercise(workout.Id, "Bench Press");

        Assert.NotEqual(bench.Id, readded.Id);
        Assert.Empty(readded.Sets);
        Assert.Equal(["Dips", "Bench Press"], (await GetWorkout(workout.Id)).Exercises.Select(e => e.ExerciseName));
    }
}
