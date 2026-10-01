using System.Net;
using System.Net.Http.Json;
using System.Text;
using GymTracker.Api;

namespace GymTracker.Tests;

/// <summary>Logging, editing and deleting sets.</summary>
public class SetTests : ApiTest
{
    private async Task<(WorkoutDto Workout, WorkoutExerciseDto Bench)> WorkoutWithBench()
    {
        var workout = await StartWorkout();
        return (workout, await AddExercise(workout.Id, "Bench Press"));
    }

    private async Task<List<SetDto>> BenchSets(int workoutId) => (await GetWorkout(workoutId)).Exercises.Single().Sets;

    private Task<HttpResponseMessage> PutSet(int setId, object body) => Client.PutAsJsonAsync($"/api/sets/{setId}", body);

    private static StringContent RawJson(string json) => new(json, Encoding.UTF8, "application/json");

    [Fact]
    public async Task Log_returns_the_saved_set()
    {
        var (_, bench) = await WorkoutWithBench();

        var set = await Read<SetDto>(await PostSet(bench.Id, new { weight = 60, reps = 8 }));

        Assert.True(set.Id > 0);
        Assert.Equal(60, set.Weight);
        Assert.Equal(8, set.Reps);
    }

    [Fact]
    public async Task Sets_are_kept_in_the_order_they_were_logged()
    {
        var (workout, bench) = await WorkoutWithBench();
        await LogSet(bench.Id, 40, 12);
        await LogSet(bench.Id, 60, 8);
        await LogSet(bench.Id, 70, 5);
        await LogSet(bench.Id, 60, 8);

        var sets = await BenchSets(workout.Id);

        Assert.Equal([(40, 12), (60, 8), (70, 5), (60, 8)], sets.Select(s => (s.Weight, s.Reps)));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0, 20)]
    [InlineData(2.5, 1)]
    [InlineData(62.5, 6)]
    [InlineData(101.25, 3)]
    [InlineData(2000, 1000)]
    public async Task Log_accepts_and_round_trips_valid_values(double weight, int reps)
    {
        var (workout, bench) = await WorkoutWithBench();

        await LogSet(bench.Id, weight, reps);

        var saved = (await BenchSets(workout.Id)).Single();
        Assert.Equal(weight, saved.Weight);
        Assert.Equal(reps, saved.Reps);
    }

    [Theory]
    [InlineData("{\"reps\":8}", "weight")]
    [InlineData("{\"weight\":null,\"reps\":8}", "weight")]
    [InlineData("{\"weight\":-0.5,\"reps\":8}", "weight")]
    [InlineData("{\"weight\":2000.5,\"reps\":8}", "weight")]
    [InlineData("{\"weight\":60}", "reps")]
    [InlineData("{\"weight\":60,\"reps\":null}", "reps")]
    [InlineData("{\"weight\":60,\"reps\":0}", "reps")]
    [InlineData("{\"weight\":60,\"reps\":-3}", "reps")]
    [InlineData("{\"weight\":60,\"reps\":1001}", "reps")]
    [InlineData("{}", "weight")]
    public async Task Log_rejects_out_of_range_or_missing_values(string json, string field)
    {
        var (workout, bench) = await WorkoutWithBench();

        var response = await Client.PostAsync($"/api/workout-exercises/{bench.Id}/sets", RawJson(json));

        await AssertValidationError(response, field);
        Assert.Empty(await BenchSets(workout.Id));
    }

    [Theory]
    [InlineData("{\"weight\":\"heavy\",\"reps\":8}")]
    [InlineData("{\"weight\":60,\"reps\":7.5}")]
    [InlineData("{\"weight\":60,\"reps\":\"many\"}")]
    [InlineData("not json")]
    [InlineData("")]
    public async Task Log_rejects_malformed_bodies_with_400(string body)
    {
        var (workout, bench) = await WorkoutWithBench();

        var response = await Client.PostAsync($"/api/workout-exercises/{bench.Id}/sets", RawJson(body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await BenchSets(workout.Id));
    }

    [Fact]
    public async Task Log_returns_404_for_an_unknown_workout_exercise()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await PostSet(9999, new { weight = 60, reps = 8 })).StatusCode);
    }

    [Fact]
    public async Task Sets_belong_to_the_exercise_they_were_logged_against()
    {
        var (workout, bench) = await WorkoutWithBench();
        var dips = await AddExercise(workout.Id, "Dips");
        await LogSet(bench.Id, 60, 8);
        await LogSet(dips.Id, 0, 15);
        await LogSet(bench.Id, 65, 5);

        var exercises = (await GetWorkout(workout.Id)).Exercises;

        Assert.Equal([(60, 8), (65, 5)], exercises[0].Sets.Select(s => (s.Weight, s.Reps)));
        Assert.Equal([(0, 15)], exercises[1].Sets.Select(s => (s.Weight, s.Reps)));
    }

    [Fact]
    public async Task Update_changes_weight_and_reps_in_place()
    {
        var (workout, bench) = await WorkoutWithBench();
        var first = await LogSet(bench.Id, 60, 8);
        var second = await LogSet(bench.Id, 60, 8);
        var third = await LogSet(bench.Id, 60, 8);

        var updated = await Read<SetDto>(await PutSet(second.Id, new { weight = 62.5, reps = 6 }));

        Assert.Equal(new SetDto(second.Id, 62.5, 6), updated);
        Assert.Equal([first, updated, third], await BenchSets(workout.Id));
    }

    [Theory]
    [InlineData("{\"weight\":-1,\"reps\":8}", "weight")]
    [InlineData("{\"weight\":2001,\"reps\":8}", "weight")]
    [InlineData("{\"reps\":8}", "weight")]
    [InlineData("{\"weight\":60,\"reps\":0}", "reps")]
    [InlineData("{\"weight\":60,\"reps\":1001}", "reps")]
    [InlineData("{\"weight\":60}", "reps")]
    public async Task Update_rejects_invalid_values_and_leaves_the_set_unchanged(string json, string field)
    {
        var (workout, bench) = await WorkoutWithBench();
        var set = await LogSet(bench.Id, 60, 8);

        var response = await Client.PutAsync($"/api/sets/{set.Id}", RawJson(json));

        await AssertValidationError(response, field);
        Assert.Equal([set], await BenchSets(workout.Id));
    }

    [Fact]
    public async Task Update_returns_404_for_an_unknown_set()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await PutSet(9999, new { weight = 60, reps = 8 })).StatusCode);
    }

    [Fact]
    public async Task Delete_removes_only_that_set_and_keeps_the_others_in_order()
    {
        var (workout, bench) = await WorkoutWithBench();
        var first = await LogSet(bench.Id, 50, 10);
        var second = await LogSet(bench.Id, 60, 8);
        var third = await LogSet(bench.Id, 70, 5);

        var response = await Client.DeleteAsync($"/api/sets/{second.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal([first, third], await BenchSets(workout.Id));
    }

    [Fact]
    public async Task A_set_logged_after_a_delete_goes_to_the_end()
    {
        var (workout, bench) = await WorkoutWithBench();
        var first = await LogSet(bench.Id, 50, 10);
        var second = await LogSet(bench.Id, 60, 8);
        var third = await LogSet(bench.Id, 70, 5);
        await Client.DeleteAsync($"/api/sets/{first.Id}");
        await Client.DeleteAsync($"/api/sets/{third.Id}");

        var fourth = await LogSet(bench.Id, 80, 3);

        Assert.Equal([second, fourth], await BenchSets(workout.Id));
    }

    [Fact]
    public async Task Delete_returns_404_for_an_unknown_set_and_when_repeated()
    {
        var (_, bench) = await WorkoutWithBench();
        var set = await LogSet(bench.Id, 60, 8);

        Assert.Equal(HttpStatusCode.NotFound, (await Client.DeleteAsync("/api/sets/9999")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"/api/sets/{set.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.DeleteAsync($"/api/sets/{set.Id}")).StatusCode);
    }

    [Fact]
    public async Task Deleting_every_set_means_the_workout_can_no_longer_be_finished()
    {
        var (workout, bench) = await WorkoutWithBench();
        var set = await LogSet(bench.Id, 60, 8);
        await Client.DeleteAsync($"/api/sets/{set.Id}");

        await AssertValidationError(await PostFinish(workout.Id), "workout");
    }

    [Fact]
    public async Task Sets_on_a_finished_workout_can_still_be_corrected()
    {
        var workout = await LogWorkout("Push", ("Bench Press", 60, 8));
        var set = workout.Exercises.Single().Sets.Single();

        await Read<SetDto>(await PutSet(set.Id, new { weight = 65, reps = 8 }));

        Assert.Equal(65, (await BenchSets(workout.Id)).Single().Weight);
        Assert.NotNull((await GetWorkout(workout.Id)).FinishedAt);
    }
}
