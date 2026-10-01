using System.Net;
using System.Net.Http.Json;
using GymTracker.Api;

namespace GymTracker.Tests;

public class ExerciseTests : ApiTest
{
    private record DeleteResult(bool Archived);

    private Task<HttpResponseMessage> Create(string? name, int? sessionTypeId = null) =>
        Client.PostAsJsonAsync("/api/exercises", new { name, sessionTypeId });

    private Task<HttpResponseMessage> Update(int id, string? name, int? sessionTypeId, bool isArchived = false) =>
        Client.PutAsJsonAsync($"/api/exercises/{id}", new { name, sessionTypeId, isArchived });

    private async Task<bool> Delete(int id) =>
        (await Read<DeleteResult>(await Client.DeleteAsync($"/api/exercises/{id}"))).Archived;

    [Fact]
    public async Task List_is_sorted_by_name_ignoring_case()
    {
        await Read<ExerciseDto>(await Create("arnold press"));
        await Read<ExerciseDto>(await Create("Zercher Squat"));

        var names = (await Exercises()).Select(e => e.Name).ToList();

        Assert.Equal(names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase), names);
        Assert.Equal("Ab Wheel Rollout", names[0]);
        Assert.Equal("arnold press", names[1]);
        Assert.Equal("Zercher Squat", names[^1]);
    }

    [Fact]
    public async Task Create_adds_an_exercise_to_a_session()
    {
        var pushId = await SessionId("Push");

        var created = await Read<ExerciseDto>(await Create("Cable Fly", pushId));

        Assert.Equal("Cable Fly", created.Name);
        Assert.Equal(pushId, created.SessionTypeId);
        Assert.False(created.IsArchived);
        Assert.Equal(created, await Exercise("Cable Fly"));
    }

    [Fact]
    public async Task Create_allows_an_exercise_without_a_session()
    {
        var created = await Read<ExerciseDto>(await Create("Farmer's Walk"));

        Assert.Null(created.SessionTypeId);
    }

    [Fact]
    public async Task Create_trims_the_name()
    {
        var created = await Read<ExerciseDto>(await Create("  Cable Fly  "));

        Assert.Equal("Cable Fly", created.Name);
    }

    [Fact]
    public async Task Create_keeps_special_characters_in_the_name()
    {
        const string name = "<b>45° Leg Press</b> & \"Sled\" – übung";

        var created = await Read<ExerciseDto>(await Create(name));

        Assert.Equal(name, created.Name);
        Assert.Equal(name, (await Exercise(name)).Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_rejects_a_blank_name(string? name)
    {
        await AssertValidationError(await Create(name), "name");

        Assert.Equal(23, (await Exercises()).Count);
    }

    [Fact]
    public async Task Create_enforces_the_name_length_limit()
    {
        await Read<ExerciseDto>(await Create(new string('x', 60)));
        await AssertValidationError(await Create(new string('y', 61)), "name");
    }

    [Theory]
    [InlineData("Bench Press")]
    [InlineData("bench press")]
    [InlineData("  BENCH PRESS ")]
    public async Task Create_rejects_a_duplicate_name_ignoring_case_and_whitespace(string name)
    {
        var message = await AssertValidationError(await Create(name, await SessionId("Pull")), "name");

        Assert.Contains("already exists", message);
        Assert.Equal(23, (await Exercises()).Count);
    }

    [Fact]
    public async Task Create_rejects_an_unknown_session()
    {
        await AssertValidationError(await Create("Cable Fly", 9999), "sessionTypeId");

        Assert.Equal(23, (await Exercises()).Count);
    }

    [Fact]
    public async Task Update_renames_and_moves_an_exercise()
    {
        var exercise = await Exercise("Dips");
        var pullId = await SessionId("Pull");

        var updated = await Read<ExerciseDto>(await Update(exercise.Id, " Weighted Dips ", pullId));

        Assert.Equal(new ExerciseDto(exercise.Id, "Weighted Dips", pullId, false), updated);
        Assert.Equal(updated, await Exercise("Weighted Dips"));
        Assert.DoesNotContain(await Exercises(), e => e.Name == "Dips");
    }

    [Fact]
    public async Task Update_can_remove_an_exercise_from_its_session()
    {
        var exercise = await Exercise("Dips");

        var updated = await Read<ExerciseDto>(await Update(exercise.Id, "Dips", null));

        Assert.Null(updated.SessionTypeId);
    }

    [Fact]
    public async Task Update_allows_keeping_or_recasing_its_own_name()
    {
        var exercise = await Exercise("Dips");

        var updated = await Read<ExerciseDto>(await Update(exercise.Id, "DIPS", exercise.SessionTypeId));

        Assert.Equal("DIPS", updated.Name);
    }

    [Fact]
    public async Task Update_rejects_another_exercises_name()
    {
        var exercise = await Exercise("Dips");

        await AssertValidationError(await Update(exercise.Id, "bench press", exercise.SessionTypeId), "name");

        Assert.Equal(exercise, await Exercise("Dips"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public async Task Update_rejects_a_blank_name(string? name)
    {
        var exercise = await Exercise("Dips");

        await AssertValidationError(await Update(exercise.Id, name, exercise.SessionTypeId), "name");
    }

    [Fact]
    public async Task Update_rejects_an_unknown_session()
    {
        var exercise = await Exercise("Dips");

        await AssertValidationError(await Update(exercise.Id, "Dips", 9999), "sessionTypeId");

        Assert.Equal(exercise, await Exercise("Dips"));
    }

    [Fact]
    public async Task Update_returns_404_for_an_unknown_exercise()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Update(9999, "Anything", null)).StatusCode);
    }

    [Fact]
    public async Task Update_can_archive_and_restore_an_exercise()
    {
        var exercise = await Exercise("Dips");

        var archived = await Read<ExerciseDto>(await Update(exercise.Id, exercise.Name, exercise.SessionTypeId, isArchived: true));
        Assert.True(archived.IsArchived);
        Assert.True((await Exercise("Dips")).IsArchived);

        var restored = await Read<ExerciseDto>(await Update(exercise.Id, exercise.Name, exercise.SessionTypeId, isArchived: false));
        Assert.Equal(exercise, restored);
    }

    [Fact]
    public async Task Delete_removes_an_exercise_that_was_never_used()
    {
        var exercise = await Exercise("Dips");

        var archived = await Delete(exercise.Id);

        Assert.False(archived);
        Assert.DoesNotContain(await Exercises(), e => e.Id == exercise.Id);
        Assert.Equal(22, (await Exercises()).Count);
    }

    [Fact]
    public async Task Delete_archives_an_exercise_that_appears_in_a_finished_workout()
    {
        var workout = await LogWorkout("Push", ("Bench Press", 60, 8));
        var exercise = await Exercise("Bench Press");

        var archived = await Delete(exercise.Id);

        Assert.True(archived);
        Assert.Equal(exercise with { IsArchived = true }, await Exercise("Bench Press"));
        Assert.Equal("Bench Press", (await GetWorkout(workout.Id)).Exercises.Single().ExerciseName);
    }

    [Fact]
    public async Task Delete_archives_an_exercise_that_is_in_the_workout_in_progress()
    {
        var workout = await StartWorkout();
        await AddExercise(workout.Id, "Bench Press");

        var archived = await Delete((await Exercise("Bench Press")).Id);

        Assert.True(archived);
        Assert.Single((await GetWorkout(workout.Id)).Exercises);
    }

    [Fact]
    public async Task Delete_removes_the_exercise_once_the_only_workout_using_it_is_gone()
    {
        var workout = await LogWorkout("Push", ("Bench Press", 60, 8));
        await Client.DeleteAsync($"/api/workouts/{workout.Id}");

        var archived = await Delete((await Exercise("Bench Press")).Id);

        Assert.False(archived);
        Assert.DoesNotContain(await Exercises(), e => e.Name == "Bench Press");
    }

    [Fact]
    public async Task Archived_exercise_names_are_still_reserved()
    {
        await LogWorkout("Push", ("Bench Press", 60, 8));
        await Delete((await Exercise("Bench Press")).Id);

        await AssertValidationError(await Create("Bench Press"), "name");
    }

    [Fact]
    public async Task A_deleted_exercises_name_can_be_reused()
    {
        var original = await Exercise("Dips");
        await Delete(original.Id);

        var recreated = await Read<ExerciseDto>(await Create("Dips"));

        Assert.NotEqual(original.Id, recreated.Id);
    }

    [Fact]
    public async Task Delete_returns_404_for_an_unknown_exercise()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Client.DeleteAsync("/api/exercises/9999")).StatusCode);
    }
}
