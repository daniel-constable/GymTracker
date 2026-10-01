using GymTracker.Api;
using GymTracker.Data;
using Microsoft.AspNetCore.Http;

namespace GymTracker.Tests;

/// <summary>Pure unit tests for the shared validation rules.</summary>
public class ValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void CheckName_rejects_missing_or_blank_names(string? input)
    {
        var error = Validation.CheckName(input, out var name);

        Assert.Equal("Name is required.", error);
        Assert.Equal("", name);
    }

    [Fact]
    public void CheckName_trims_surrounding_whitespace()
    {
        var error = Validation.CheckName("  Bench Press \t", out var name);

        Assert.Null(error);
        Assert.Equal("Bench Press", name);
    }

    [Fact]
    public void CheckName_accepts_a_name_at_the_maximum_length()
    {
        var input = new string('a', GymDbContext.NameMaxLength);

        Assert.Null(Validation.CheckName(input, out var name));
        Assert.Equal(input, name);
    }

    [Fact]
    public void CheckName_rejects_a_name_over_the_maximum_length()
    {
        var error = Validation.CheckName(new string('a', GymDbContext.NameMaxLength + 1), out _);

        Assert.NotNull(error);
        Assert.Contains("60", error);
    }

    [Fact]
    public void CheckName_measures_length_after_trimming()
    {
        var input = "  " + new string('a', GymDbContext.NameMaxLength) + "  ";

        Assert.Null(Validation.CheckName(input, out _));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0.5, 1)]
    [InlineData(62.5, 8)]
    [InlineData(Validation.MaxWeight, Validation.MaxReps)]
    public void CheckSet_accepts_values_within_range(double weight, int reps)
    {
        Assert.Null(Validation.CheckSet(new SetRequest(weight, reps)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-0.01)]
    [InlineData(-50d)]
    [InlineData(Validation.MaxWeight + 0.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void CheckSet_rejects_invalid_weight(double? weight)
    {
        var problem = Validation.CheckSet(new SetRequest(weight, 8));

        Assert.Equal(["weight"], ErrorFields(problem));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(Validation.MaxReps + 1)]
    [InlineData(int.MaxValue)]
    public void CheckSet_rejects_invalid_reps(int? reps)
    {
        var problem = Validation.CheckSet(new SetRequest(60, reps));

        Assert.Equal(["reps"], ErrorFields(problem));
    }

    [Fact]
    public void Problem_builds_a_400_with_the_message_under_the_field()
    {
        var result = Assert.IsAssignableFrom<IValueHttpResult>(Validation.Problem("name", "Nope."));
        var details = Assert.IsType<HttpValidationProblemDetails>(result.Value);

        Assert.Equal(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.Equal(["Nope."], details.Errors["name"]);
    }

    private static IEnumerable<string> ErrorFields(IResult? problem)
    {
        var result = Assert.IsAssignableFrom<IValueHttpResult>(problem);
        return Assert.IsType<HttpValidationProblemDetails>(result.Value).Errors.Keys;
    }
}
