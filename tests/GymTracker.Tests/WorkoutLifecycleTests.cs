using System.Net;
using GymTracker.Api;

namespace GymTracker.Tests;

/// <summary>Starting, resuming, finishing, discarding and deleting workouts.</summary>
public class WorkoutLifecycleTests : ApiTest
{
    private async Task<HttpStatusCode> ActiveStatus() => (await Client.GetAsync("/api/workouts/active")).StatusCode;

    [Fact]
    public async Task Start_creates_an_empty_workout_in_progress_named_after_the_session()
    {
        var before = DateTime.UtcNow;
        var legsId = await SessionId("Legs");

        var workout = await Read<WorkoutDto>(await PostStartWorkout(legsId));

        Assert.Equal("Legs", workout.Name);
        Assert.Equal(legsId, workout.SessionTypeId);
        Assert.Null(workout.FinishedAt);
        Assert.Empty(workout.Exercises);
        Assert.Equal(DateTimeKind.Utc, workout.StartedAt.Kind);
        Assert.InRange(workout.StartedAt, before.AddSeconds(-1), DateTime.UtcNow.AddSeconds(1));
    }

    [Fact]
    public async Task Timestamps_are_serialised_as_utc()
    {
        var workout = await LogWorkout("Push", ("Bench Press", 60, 8));

        var json = await Client.GetStringAsync($"/api/workouts/{workout.Id}");

        Assert.Matches("\"startedAt\":\"[^\"]+Z\"", json);
        Assert.Matches("\"finishedAt\":\"[^\"]+Z\"", json);
    }

    [Fact]
    public async Task Start_rejects_a_missing_session()
    {
        await AssertValidationError(await PostStartWorkout(null), "sessionTypeId");

        Assert.Equal(HttpStatusCode.NoContent, await ActiveStatus());
    }

    [Fact]
    public async Task Start_rejects_an_unknown_session()
    {
        await AssertValidationError(await PostStartWorkout(9999), "sessionTypeId");

        Assert.Equal(HttpStatusCode.NoContent, await ActiveStatus());
    }

    [Fact]
    public async Task Start_is_rejected_while_another_workout_is_in_progress()
    {
        var first = await StartWorkout("Push");

        var message = await AssertValidationError(await PostStartWorkout(await SessionId("Pull")), "workout");

        Assert.Contains("already in progress", message);
        Assert.Equal(first.Id, (await Get<WorkoutDto>("/api/workouts/active")).Id);
    }

    [Fact]
    public async Task Active_returns_204_when_nothing_is_in_progress()
    {
        Assert.Equal(HttpStatusCode.NoContent, await ActiveStatus());
    }

    [Fact]
    public async Task Active_returns_the_workout_in_progress_with_everything_logged_so_far()
    {
        var workout = await StartWorkout("Push");
        var bench = await AddExercise(workout.Id, "Bench Press");
        await LogSet(bench.Id, 60, 8);
        await LogSet(bench.Id, 62.5, 6);
        await AddExercise(workout.Id, "Dips");

        var active = await Get<WorkoutDto>("/api/workouts/active");

        Assert.Equal(workout.Id, active.Id);
        Assert.Equal(["Bench Press", "Dips"], active.Exercises.Select(e => e.ExerciseName));
        Assert.Equal([(60, 8), (62.5, 6)], active.Exercises[0].Sets.Select(s => (s.Weight, s.Reps)));
        Assert.Empty(active.Exercises[1].Sets);
    }

    [Fact]
    public async Task Get_returns_a_workout_by_id()
    {
        var workout = await StartWorkout("Core");

        var loaded = await GetWorkout(workout.Id);

        Assert.Equal(workout.Id, loaded.Id);
        Assert.Equal("Core", loaded.Name);
        Assert.Equal(workout.StartedAt, loaded.StartedAt);
    }

    [Fact]
    public async Task Get_returns_404_for_an_unknown_workout()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync("/api/workouts/9999")).StatusCode);
    }

    [Fact]
    public async Task Finish_is_rejected_when_the_workout_has_no_exercises()
    {
        var workout = await StartWorkout();

        var message = await AssertValidationError(await PostFinish(workout.Id), "workout");

        Assert.Contains("at least one set", message);
        Assert.Null((await GetWorkout(workout.Id)).FinishedAt);
    }

    [Fact]
    public async Task Finish_is_rejected_when_no_sets_have_been_logged()
    {
        var workout = await StartWorkout();
        await AddExercise(workout.Id, "Bench Press");
        await AddExercise(workout.Id, "Dips");

        await AssertValidationError(await PostFinish(workout.Id), "workout");

        var still = await Get<WorkoutDto>("/api/workouts/active");
        Assert.Equal(workout.Id, still.Id);
        Assert.Equal(2, still.Exercises.Count);
    }

    [Fact]
    public async Task Finish_stamps_the_end_time_and_clears_the_active_workout()
    {
        var workout = await StartWorkout();
        var bench = await AddExercise(workout.Id, "Bench Press");
        await LogSet(bench.Id, 60, 8);

        var finished = await Finish(workout.Id);

        Assert.NotNull(finished.FinishedAt);
        Assert.Equal(DateTimeKind.Utc, finished.FinishedAt.Value.Kind);
        Assert.True(finished.FinishedAt >= finished.StartedAt);
        Assert.InRange(finished.FinishedAt.Value, workout.StartedAt, DateTime.UtcNow.AddSeconds(1));
        Assert.Equal(HttpStatusCode.NoContent, await ActiveStatus());
        Assert.Equal(finished.FinishedAt, (await GetWorkout(workout.Id)).FinishedAt);
    }

    [Fact]
    public async Task Finish_drops_exercises_that_have_no_sets_and_keeps_the_rest_in_order()
    {
        var workout = await StartWorkout();
        await AddExercise(workout.Id, "Bench Press");
        var dips = await AddExercise(workout.Id, "Dips");
        await AddExercise(workout.Id, "Lateral Raise");
        var press = await AddExercise(workout.Id, "Overhead Press");
        await LogSet(dips.Id, 0, 12);
        await LogSet(press.Id, 40, 8);

        var finished = await Finish(workout.Id);

        Assert.Equal(["Dips", "Overhead Press"], finished.Exercises.Select(e => e.ExerciseName));
        Assert.Equal(["Dips", "Overhead Press"], (await GetWorkout(workout.Id)).Exercises.Select(e => e.ExerciseName));
    }

    [Fact]
    public async Task Finish_is_idempotent()
    {
        var finished = await LogWorkout("Push", ("Bench Press", 60, 8));

        var again = await Finish(finished.Id);

        Assert.Equal(finished.FinishedAt, again.FinishedAt);
        Assert.Single(await History());
    }

    [Fact]
    public async Task Finish_returns_404_for_an_unknown_workout()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await PostFinish(9999)).StatusCode);
    }

    [Fact]
    public async Task A_new_workout_can_be_started_after_finishing()
    {
        var first = await LogWorkout("Push", ("Bench Press", 60, 8));

        var second = await StartWorkout("Pull");

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(second.Id, (await Get<WorkoutDto>("/api/workouts/active")).Id);
    }

    [Fact]
    public async Task The_same_session_can_be_done_many_times()
    {
        await LogWorkout("Push", ("Bench Press", 60, 8));
        await LogWorkout("Push", ("Bench Press", 62.5, 8));
        await LogWorkout("Push", ("Bench Press", 65, 8));

        Assert.Equal(["Push", "Push", "Push"], (await History()).Select(w => w.Name));
    }

    [Fact]
    public async Task Discarding_the_workout_in_progress_deletes_it_and_everything_in_it()
    {
        var workout = await StartWorkout();
        var bench = await AddExercise(workout.Id, "Bench Press");
        var set = await LogSet(bench.Id, 60, 8);

        var response = await Client.DeleteAsync($"/api/workouts/{workout.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, await ActiveStatus());
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/workouts/{workout.Id}")).StatusCode);
        Assert.Empty(await History());
        // The exercise entry and its set went with it.
        Assert.Equal(HttpStatusCode.NotFound, (await Client.DeleteAsync($"/api/workout-exercises/{bench.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.DeleteAsync($"/api/sets/{set.Id}")).StatusCode);
    }

    [Fact]
    public async Task A_new_workout_can_be_started_after_discarding()
    {
        var discarded = await StartWorkout("Push");
        await Client.DeleteAsync($"/api/workouts/{discarded.Id}");

        var next = await StartWorkout("Push");

        Assert.NotEqual(discarded.Id, next.Id);
    }

    [Fact]
    public async Task Deleting_a_finished_workout_removes_it_from_history_only()
    {
        var first = await LogWorkout("Push", ("Bench Press", 60, 8));
        var second = await LogWorkout("Pull", ("Deadlift", 100, 5));

        var response = await Client.DeleteAsync($"/api/workouts/{first.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal([second.Id], (await History()).Select(w => w.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/workouts/{first.Id}")).StatusCode);
    }

    [Fact]
    public async Task Delete_returns_404_for_an_unknown_workout()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Client.DeleteAsync("/api/workouts/9999")).StatusCode);
    }

    [Fact]
    public async Task A_workout_in_progress_survives_an_app_restart()
    {
        var path = GymApp.NewDatabasePath();
        try
        {
            int workoutId;
            using (var first = new GymApp(path))
            using (var client = first.CreateClient())
            {
                var pushId = (await Read<List<SessionTypeDto>>(await client.GetAsync("/api/session-types")))[0].Id;
                var benchId = (await Read<List<ExerciseDto>>(await client.GetAsync("/api/exercises"))).Single(e => e.Name == "Bench Press").Id;
                var workout = await Read<WorkoutDto>(await client.PostAsync("/api/workouts", JsonBody(new { sessionTypeId = pushId })));
                var entry = await Read<WorkoutExerciseDto>(await client.PostAsync($"/api/workouts/{workout.Id}/exercises", JsonBody(new { exerciseId = benchId })));
                await Read<SetDto>(await client.PostAsync($"/api/workout-exercises/{entry.Id}/sets", JsonBody(new { weight = 60, reps = 8 })));
                workoutId = workout.Id;
            }

            using (var second = new GymApp(path))
            using (var client = second.CreateClient())
            {
                var active = await Read<WorkoutDto>(await client.GetAsync("/api/workouts/active"));

                Assert.Equal(workoutId, active.Id);
                Assert.Equal((60, 8), active.Exercises.Single().Sets.Select(s => (s.Weight, s.Reps)).Single());
            }
        }
        finally
        {
            GymApp.DeleteDatabase(path);
        }
    }

    private static System.Net.Http.Json.JsonContent JsonBody(object value) => System.Net.Http.Json.JsonContent.Create(value);
}
