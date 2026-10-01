using GymTracker.Data;

namespace GymTracker.Api;

public static class Validation
{
    public const double MaxWeight = 2000;
    public const int MaxReps = 1000;

    public static IResult Problem(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    /// <summary>Trims the name and returns an error message if it is unusable.</summary>
    public static string? CheckName(string? input, out string name)
    {
        name = (input ?? "").Trim();
        if (name.Length == 0) return "Name is required.";
        if (name.Length > GymDbContext.NameMaxLength) return $"Name must be {GymDbContext.NameMaxLength} characters or fewer.";
        return null;
    }

    public static IResult? CheckSet(SetRequest req)
    {
        if (req.Weight is not { } weight || !double.IsFinite(weight) || weight < 0 || weight > MaxWeight)
            return Problem("weight", $"Weight must be between 0 and {MaxWeight}.");
        if (req.Reps is not { } reps || reps < 1 || reps > MaxReps)
            return Problem("reps", $"Reps must be between 1 and {MaxReps}.");
        return null;
    }
}
