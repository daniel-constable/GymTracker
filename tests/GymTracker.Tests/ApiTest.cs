using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GymTracker.Api;

namespace GymTracker.Tests;

/// <summary>Base class: each test gets a fresh app and a freshly seeded database.</summary>
public abstract class ApiTest : IDisposable
{
    protected ApiTest()
    {
        App = new GymApp();
        Client = App.CreateClient();
    }

    protected GymApp App { get; }
    protected HttpClient Client { get; }

    public void Dispose()
    {
        Client.Dispose();
        App.Dispose();
        GC.SuppressFinalize(this);
    }

    // ----- Reading responses -----

    protected static async Task<T> Read<T>(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var value = await response.Content.ReadFromJsonAsync<T>();
        Assert.NotNull(value);
        return value;
    }

    protected async Task<T> Get<T>(string url) => await Read<T>(await Client.GetAsync(url));

    /// <summary>Asserts a 400 validation problem that reports an error against the given field.</summary>
    protected static async Task<string> AssertValidationError(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.TryGetProperty("errors", out var errors), "Response has no 'errors' object.");
        Assert.True(errors.TryGetProperty(field, out var messages), $"No error reported for '{field}': {errors}");
        var message = messages[0].GetString();
        Assert.False(string.IsNullOrWhiteSpace(message));
        return message!;
    }

    // ----- Sessions and exercises -----

    protected Task<List<SessionTypeDto>> Sessions() => Get<List<SessionTypeDto>>("/api/session-types");

    protected async Task<int> SessionId(string name) => (await Sessions()).Single(s => s.Name == name).Id;

    protected Task<List<ExerciseDto>> Exercises() => Get<List<ExerciseDto>>("/api/exercises");

    protected async Task<ExerciseDto> Exercise(string name) => (await Exercises()).Single(e => e.Name == name);

    // ----- Workouts -----

    protected Task<HttpResponseMessage> PostStartWorkout(int? sessionTypeId) =>
        Client.PostAsJsonAsync("/api/workouts", new { sessionTypeId });

    protected async Task<WorkoutDto> StartWorkout(string session = "Push") =>
        await Read<WorkoutDto>(await PostStartWorkout(await SessionId(session)));

    protected Task<HttpResponseMessage> PostWorkoutExercise(int workoutId, int? exerciseId) =>
        Client.PostAsJsonAsync($"/api/workouts/{workoutId}/exercises", new { exerciseId });

    protected async Task<WorkoutExerciseDto> AddExercise(int workoutId, string exerciseName) =>
        await Read<WorkoutExerciseDto>(await PostWorkoutExercise(workoutId, (await Exercise(exerciseName)).Id));

    protected Task<HttpResponseMessage> PostSet(int workoutExerciseId, object body) =>
        Client.PostAsJsonAsync($"/api/workout-exercises/{workoutExerciseId}/sets", body);

    protected async Task<SetDto> LogSet(int workoutExerciseId, double weight, int reps) =>
        await Read<SetDto>(await PostSet(workoutExerciseId, new { weight, reps }));

    protected Task<HttpResponseMessage> PostFinish(int workoutId) =>
        Client.PostAsync($"/api/workouts/{workoutId}/finish", null);

    protected async Task<WorkoutDto> Finish(int workoutId) => await Read<WorkoutDto>(await PostFinish(workoutId));

    protected Task<WorkoutDto> GetWorkout(int id) => Get<WorkoutDto>($"/api/workouts/{id}");

    protected Task<List<WorkoutSummaryDto>> History(string query = "") =>
        Get<List<WorkoutSummaryDto>>($"/api/workouts{query}");

    /// <summary>Starts, fills and finishes a whole workout. Sets are grouped by exercise in order of first appearance.</summary>
    protected async Task<WorkoutDto> LogWorkout(string session, params (string Exercise, double Weight, int Reps)[] sets)
    {
        var workout = await StartWorkout(session);
        var entries = new Dictionary<string, int>();
        foreach (var (exercise, weight, reps) in sets)
        {
            if (!entries.TryGetValue(exercise, out var entryId))
                entries[exercise] = entryId = (await AddExercise(workout.Id, exercise)).Id;
            await LogSet(entryId, weight, reps);
        }
        return await Finish(workout.Id);
    }
}
