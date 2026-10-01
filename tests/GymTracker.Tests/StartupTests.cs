using System.Net;
using System.Net.Http.Json;
using GymTracker.Api;

namespace GymTracker.Tests;

/// <summary>First-run seeding, restarts, configuration and the static front end.</summary>
public class StartupTests : ApiTest
{
    [Fact]
    public async Task New_database_is_seeded_with_the_four_default_sessions_in_order()
    {
        var sessions = await Sessions();

        Assert.Equal(["Push", "Pull", "Legs", "Core"], sessions.Select(s => s.Name));
    }

    [Theory]
    [InlineData("Push", 6)]
    [InlineData("Pull", 6)]
    [InlineData("Legs", 6)]
    [InlineData("Core", 5)]
    public async Task New_database_is_seeded_with_starter_exercises_for_each_session(string session, int expected)
    {
        var sessionId = await SessionId(session);
        var exercises = await Exercises();

        Assert.Equal(expected, exercises.Count(e => e.SessionTypeId == sessionId));
    }

    [Fact]
    public async Task Seeded_exercises_are_all_active_and_grouped()
    {
        var exercises = await Exercises();

        Assert.Equal(23, exercises.Count);
        Assert.All(exercises, e => Assert.False(e.IsArchived));
        Assert.All(exercises, e => Assert.NotNull(e.SessionTypeId));
        Assert.Equal((await Exercise("Bench Press")).SessionTypeId, await SessionId("Push"));
        Assert.Equal((await Exercise("Squat")).SessionTypeId, await SessionId("Legs"));
    }

    [Fact]
    public async Task New_database_has_no_workouts()
    {
        Assert.Empty(await History());
        Assert.Equal(HttpStatusCode.NoContent, (await Client.GetAsync("/api/workouts/active")).StatusCode);
    }

    [Fact]
    public async Task Restarting_keeps_existing_data_and_does_not_seed_again()
    {
        var path = GymApp.NewDatabasePath();
        try
        {
            int workoutId;
            using (var first = new GymApp(path))
            using (var client = first.CreateClient())
            {
                var push = (await client.GetFromJsonAsync<List<SessionTypeDto>>("/api/session-types"))!.First();
                var workout = await Read<WorkoutDto>(await client.PostAsJsonAsync("/api/workouts", new { sessionTypeId = push.Id }));
                workoutId = workout.Id;
                await Read<ExerciseDto>(await client.PostAsJsonAsync("/api/exercises", new { name = "Cable Fly", sessionTypeId = push.Id }));
            }

            using (var second = new GymApp(path))
            using (var client = second.CreateClient())
            {
                var sessions = await client.GetFromJsonAsync<List<SessionTypeDto>>("/api/session-types");
                var exercises = await client.GetFromJsonAsync<List<ExerciseDto>>("/api/exercises");
                var active = await client.GetFromJsonAsync<WorkoutDto>("/api/workouts/active");

                Assert.Equal(4, sessions!.Count);
                Assert.Equal(24, exercises!.Count);
                Assert.Contains(exercises, e => e.Name == "Cable Fly");
                Assert.Equal(workoutId, active!.Id);
            }
        }
        finally
        {
            GymApp.DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task Restarting_does_not_bring_back_sessions_the_user_deleted()
    {
        var path = GymApp.NewDatabasePath();
        try
        {
            using (var first = new GymApp(path))
            using (var client = first.CreateClient())
            {
                foreach (var session in (await client.GetFromJsonAsync<List<SessionTypeDto>>("/api/session-types"))!)
                    Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/session-types/{session.Id}")).StatusCode);
            }

            using (var second = new GymApp(path))
            using (var client = second.CreateClient())
            {
                Assert.Empty((await client.GetFromJsonAsync<List<SessionTypeDto>>("/api/session-types"))!);
                Assert.Equal(23, (await client.GetFromJsonAsync<List<ExerciseDto>>("/api/exercises"))!.Count);
            }
        }
        finally
        {
            GymApp.DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task Each_app_instance_gets_its_own_database()
    {
        await Read<SessionTypeDto>(await Client.PostAsJsonAsync("/api/session-types", new { name = "Upper" }));

        using var other = new GymApp();
        using var otherClient = other.CreateClient();
        var sessions = await otherClient.GetFromJsonAsync<List<SessionTypeDto>>("/api/session-types");

        Assert.DoesNotContain(sessions!, s => s.Name == "Upper");
    }

    [Fact]
    public async Task Config_reports_kg_by_default()
    {
        var config = await Get<Dictionary<string, string>>("/api/config");

        Assert.Equal("kg", config["weightUnit"]);
    }

    [Fact]
    public async Task Config_reports_the_configured_weight_unit()
    {
        using var app = new GymApp(settings: new() { ["WeightUnit"] = "lb" });
        using var client = app.CreateClient();

        var config = await client.GetFromJsonAsync<Dictionary<string, string>>("/api/config");

        Assert.Equal("lb", config!["weightUnit"]);
    }

    [Fact]
    public async Task Root_serves_the_app_shell()
    {
        var response = await Client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<title>Gym Tracker</title>", html);
        Assert.Contains("js/app.js", html);
    }

    [Theory]
    [InlineData("/index.html", "text/html")]
    [InlineData("/app.css", "text/css")]
    [InlineData("/js/app.js", "text/javascript")]
    [InlineData("/js/api.js", "text/javascript")]
    [InlineData("/js/util.js", "text/javascript")]
    [InlineData("/js/chart.js", "text/javascript")]
    [InlineData("/js/views/home.js", "text/javascript")]
    [InlineData("/js/views/workout.js", "text/javascript")]
    [InlineData("/js/views/history.js", "text/javascript")]
    [InlineData("/js/views/exercises.js", "text/javascript")]
    [InlineData("/js/views/progress.js", "text/javascript")]
    public async Task Front_end_files_are_served_and_always_revalidated(string path, string contentType)
    {
        var response = await Client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(contentType, response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoCache);
    }

    [Theory]
    [InlineData("/api/nope")]
    [InlineData("/missing.js")]
    [InlineData("/api/workouts/not-a-number")]
    public async Task Unknown_paths_return_404(string path)
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync(path)).StatusCode);
    }
}
