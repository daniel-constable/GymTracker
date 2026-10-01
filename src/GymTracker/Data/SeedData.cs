using Microsoft.EntityFrameworkCore;

namespace GymTracker.Data;

public static class SeedData
{
    private static readonly (string Session, string[] Exercises)[] Defaults =
    [
        ("Push", ["Bench Press", "Incline Dumbbell Press", "Overhead Press", "Lateral Raise", "Tricep Pushdown", "Dips"]),
        ("Pull", ["Deadlift", "Pull-Up", "Barbell Row", "Lat Pulldown", "Face Pull", "Bicep Curl"]),
        ("Legs", ["Squat", "Romanian Deadlift", "Leg Press", "Leg Curl", "Leg Extension", "Calf Raise"]),
        ("Core", ["Plank", "Hanging Leg Raise", "Cable Crunch", "Ab Wheel Rollout", "Russian Twist"]),
    ];

    /// <summary>Adds starter session types and exercises to a brand-new database.</summary>
    public static async Task EnsureSeededAsync(GymDbContext db)
    {
        if (await db.SessionTypes.AnyAsync() || await db.Exercises.AnyAsync() || await db.Workouts.AnyAsync())
            return;

        var order = 0;
        foreach (var (session, exercises) in Defaults)
        {
            db.SessionTypes.Add(new SessionType
            {
                Name = session,
                SortOrder = order++,
                Exercises = exercises.Select(name => new Exercise { Name = name }).ToList(),
            });
        }

        await db.SaveChangesAsync();
    }
}
