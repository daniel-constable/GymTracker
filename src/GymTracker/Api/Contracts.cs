namespace GymTracker.Api;

// Requests. Value fields are nullable so a missing field is reported as a validation error
// instead of silently becoming 0.
public record NameRequest(string? Name);
public record ExerciseRequest(string? Name, int? SessionTypeId, bool IsArchived = false);
public record StartWorkoutRequest(int? SessionTypeId);
public record AddWorkoutExerciseRequest(int? ExerciseId);
public record SetRequest(double? Weight, int? Reps);

// Responses.
public record SessionTypeDto(int Id, string Name);
public record ExerciseDto(int Id, string Name, int? SessionTypeId, bool IsArchived);
public record SetDto(int Id, double Weight, int Reps);
public record PreviousPerformanceDto(int WorkoutId, DateTime Date, List<SetDto> Sets);
public record WorkoutExerciseDto(int Id, int ExerciseId, string ExerciseName, List<SetDto> Sets, PreviousPerformanceDto? Previous);
public record WorkoutDto(int Id, string Name, int? SessionTypeId, DateTime StartedAt, DateTime? FinishedAt, List<WorkoutExerciseDto> Exercises);
public record WorkoutSummaryDto(int Id, string Name, DateTime StartedAt, DateTime? FinishedAt, int SetCount, List<string> ExerciseNames);

// Progress for a single exercise.
public record ExerciseProgressSummaryDto(int ExerciseId, string Name, int SessionCount, DateTime LastPerformed);

/// <summary>One finished workout's worth of an exercise, with the figures derived from its sets.</summary>
public record ProgressSessionDto(
    int WorkoutId, string WorkoutName, DateTime Date, List<SetDto> Sets,
    double TopWeight, int TopWeightReps, int BestReps, double Volume, double? EstimatedOneRepMax);

/// <summary>Weight and Reps identify the set that earned the record; they are null for whole-session records.</summary>
public record PersonalRecordDto(double Value, double? Weight, int? Reps, int WorkoutId, DateTime Date);

public record ProgressRecordsDto(
    PersonalRecordDto HeaviestSet, PersonalRecordDto? BestOneRepMax, PersonalRecordDto MostReps, PersonalRecordDto BestVolume);

/// <summary>The latest session compared with an earlier one. Period is "month", "threeMonths", "year" or "start".</summary>
public record ProgressChangeDto(
    string Period, int WorkoutId, DateTime Date,
    double? OneRepMaxThen, double? OneRepMaxNow,
    double TopWeightThen, double TopWeightNow,
    int BestRepsThen, int BestRepsNow);

public record ExerciseProgressDto(
    int ExerciseId, string Name, bool IsArchived,
    List<ProgressSessionDto> Sessions, ProgressRecordsDto? Records, List<ProgressChangeDto> Changes);
