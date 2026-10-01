namespace GymTracker.Data;

/// <summary>A kind of workout, e.g. Push, Pull, Legs, Core.</summary>
public class SessionType
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int SortOrder { get; set; }
    public List<Exercise> Exercises { get; set; } = [];
}

public class Exercise
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int? SessionTypeId { get; set; }
    public SessionType? SessionType { get; set; }

    /// <summary>Archived exercises are hidden from the picker but keep their history.</summary>
    public bool IsArchived { get; set; }
}

public class Workout
{
    public int Id { get; set; }

    /// <summary>Copy of the session type's name, so history survives renames and deletes.</summary>
    public required string Name { get; set; }
    public int? SessionTypeId { get; set; }
    public SessionType? SessionType { get; set; }
    public DateTime StartedAt { get; set; }

    /// <summary>Null while the workout is in progress.</summary>
    public DateTime? FinishedAt { get; set; }
    public List<WorkoutExercise> Exercises { get; set; } = [];
}

public class WorkoutExercise
{
    public int Id { get; set; }
    public int WorkoutId { get; set; }
    public Workout Workout { get; set; } = null!;
    public int ExerciseId { get; set; }
    public Exercise Exercise { get; set; } = null!;
    public int Position { get; set; }
    public List<SetEntry> Sets { get; set; } = [];
}

public class SetEntry
{
    public int Id { get; set; }
    public int WorkoutExerciseId { get; set; }
    public WorkoutExercise WorkoutExercise { get; set; } = null!;
    public int Position { get; set; }
    public double Weight { get; set; }
    public int Reps { get; set; }
}
