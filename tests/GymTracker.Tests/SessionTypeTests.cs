using System.Net;
using System.Net.Http.Json;
using GymTracker.Api;

namespace GymTracker.Tests;

public class SessionTypeTests : ApiTest
{
    private Task<HttpResponseMessage> Create(string? name) => Client.PostAsJsonAsync("/api/session-types", new { name });
    private Task<HttpResponseMessage> Rename(int id, string? name) => Client.PutAsJsonAsync($"/api/session-types/{id}", new { name });

    [Fact]
    public async Task Create_adds_the_session_to_the_end_of_the_list()
    {
        var created = await Read<SessionTypeDto>(await Create("Upper Body"));

        var sessions = await Sessions();
        Assert.Equal("Upper Body", created.Name);
        Assert.Equal(["Push", "Pull", "Legs", "Core", "Upper Body"], sessions.Select(s => s.Name));
        Assert.Equal(created.Id, sessions.Last().Id);
    }

    [Fact]
    public async Task Create_trims_the_name()
    {
        var created = await Read<SessionTypeDto>(await Create("  Cardio  "));

        Assert.Equal("Cardio", created.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_rejects_a_blank_name(string? name)
    {
        await AssertValidationError(await Create(name), "name");

        Assert.Equal(4, (await Sessions()).Count);
    }

    [Fact]
    public async Task Create_rejects_a_missing_name()
    {
        await AssertValidationError(await Client.PostAsJsonAsync("/api/session-types", new { }), "name");
    }

    [Fact]
    public async Task Create_enforces_the_name_length_limit()
    {
        await Read<SessionTypeDto>(await Create(new string('x', 60)));
        await AssertValidationError(await Create(new string('y', 61)), "name");
    }

    [Theory]
    [InlineData("Push")]
    [InlineData("push")]
    [InlineData(" PUSH ")]
    public async Task Create_rejects_a_duplicate_name_ignoring_case_and_whitespace(string name)
    {
        var message = await AssertValidationError(await Create(name), "name");

        Assert.Contains("already exists", message);
        Assert.Equal(4, (await Sessions()).Count);
    }

    [Fact]
    public async Task Rename_changes_the_name_and_keeps_the_position()
    {
        var pullId = await SessionId("Pull");

        var renamed = await Read<SessionTypeDto>(await Rename(pullId, "Back & Biceps"));

        Assert.Equal(new SessionTypeDto(pullId, "Back & Biceps"), renamed);
        Assert.Equal(["Push", "Back & Biceps", "Legs", "Core"], (await Sessions()).Select(s => s.Name));
    }

    [Fact]
    public async Task Rename_allows_changing_only_the_case_of_its_own_name()
    {
        var renamed = await Read<SessionTypeDto>(await Rename(await SessionId("Push"), "PUSH"));

        Assert.Equal("PUSH", renamed.Name);
    }

    [Fact]
    public async Task Rename_rejects_another_sessions_name()
    {
        await AssertValidationError(await Rename(await SessionId("Pull"), "push"), "name");

        Assert.Equal(["Push", "Pull", "Legs", "Core"], (await Sessions()).Select(s => s.Name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task Rename_rejects_a_blank_name(string? name)
    {
        await AssertValidationError(await Rename(await SessionId("Pull"), name), "name");
    }

    [Fact]
    public async Task Rename_returns_404_for_an_unknown_session()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Rename(9999, "Anything")).StatusCode);
    }

    [Fact]
    public async Task Rename_does_not_change_the_name_on_past_workouts()
    {
        var workout = await LogWorkout("Push", ("Bench Press", 60, 8));

        await Read<SessionTypeDto>(await Rename(await SessionId("Push"), "Chest Day"));

        Assert.Equal("Push", (await GetWorkout(workout.Id)).Name);
        Assert.Equal("Push", (await History()).Single().Name);
    }

    [Fact]
    public async Task Delete_removes_the_session()
    {
        var response = await Client.DeleteAsync($"/api/session-types/{await SessionId("Core")}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(["Push", "Pull", "Legs"], (await Sessions()).Select(s => s.Name));
    }

    [Fact]
    public async Task Delete_keeps_the_sessions_exercises_but_ungroups_them()
    {
        var coreId = await SessionId("Core");
        var coreExercises = (await Exercises()).Where(e => e.SessionTypeId == coreId).Select(e => e.Id).ToList();

        await Client.DeleteAsync($"/api/session-types/{coreId}");

        var after = await Exercises();
        Assert.Equal(23, after.Count);
        Assert.All(after.Where(e => coreExercises.Contains(e.Id)), e => Assert.Null(e.SessionTypeId));
        Assert.Equal(18, after.Count(e => e.SessionTypeId is not null));
    }

    [Fact]
    public async Task Delete_keeps_past_workouts_with_their_name()
    {
        var workout = await LogWorkout("Push", ("Bench Press", 60, 8));

        var response = await Client.DeleteAsync($"/api/session-types/{await SessionId("Push")}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var kept = await GetWorkout(workout.Id);
        Assert.Equal("Push", kept.Name);
        Assert.Null(kept.SessionTypeId);
        Assert.Single(kept.Exercises.Single().Sets);
    }

    [Fact]
    public async Task Delete_is_blocked_while_a_workout_of_that_session_is_in_progress()
    {
        await StartWorkout("Push");

        await AssertValidationError(await Client.DeleteAsync($"/api/session-types/{await SessionId("Push")}"), "id");

        Assert.Contains(await Sessions(), s => s.Name == "Push");
    }

    [Fact]
    public async Task Delete_of_a_different_session_is_allowed_while_a_workout_is_in_progress()
    {
        await StartWorkout("Push");

        var response = await Client.DeleteAsync($"/api/session-types/{await SessionId("Core")}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Delete_returns_404_for_an_unknown_session()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Client.DeleteAsync("/api/session-types/9999")).StatusCode);
    }

    [Fact]
    public async Task A_deleted_sessions_name_can_be_reused()
    {
        await Client.DeleteAsync($"/api/session-types/{await SessionId("Core")}");

        var recreated = await Read<SessionTypeDto>(await Create("Core"));

        Assert.Equal("Core", (await Sessions()).Last().Name);
        Assert.Equal(recreated.Id, (await Sessions()).Last().Id);
    }
}
