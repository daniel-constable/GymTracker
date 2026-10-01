namespace GymTracker.Api;

/// <summary>Turns an exercise's logged sets into per-session figures, personal records and then-vs-now comparisons.</summary>
public static class ProgressCalculator
{
    /// <summary>Rep-max formulas get unreliable for long sets, so sets above this many reps are not used for 1RM.</summary>
    public const int MaxRepsForOneRepMax = 12;

    // Longest first: when two periods land on the same earlier session, the longer one is kept.
    private static readonly (string Period, int Days)[] Periods = [("year", 365), ("threeMonths", 90), ("month", 30)];
    private static readonly string[] PeriodOrder = ["month", "threeMonths", "year", "start"];

    public record SessionInput(int WorkoutId, string WorkoutName, DateTime Date, IReadOnlyList<SetDto> Sets);

    /// <summary>
    /// Estimated one-rep max using the Epley formula: weight × (1 + reps / 30). A single rep is its own max.
    /// Returns null when it can't be estimated: no weight on the bar, or too many reps to be meaningful.
    /// </summary>
    public static double? EstimateOneRepMax(double weight, int reps)
    {
        if (weight <= 0 || reps < 1 || reps > MaxRepsForOneRepMax) return null;
        return reps == 1 ? weight : Math.Round(weight * (1 + reps / 30.0), 1);
    }

    public static ExerciseProgressDto Build(int exerciseId, string name, bool isArchived, IEnumerable<SessionInput> inputs, DateTime now)
    {
        var sessions = inputs
            .Where(i => i.Sets.Count > 0)
            .OrderBy(i => i.Date)
            .Select(ToSession)
            .ToList();

        return new ExerciseProgressDto(exerciseId, name, isArchived, sessions, FindRecords(sessions), FindChanges(sessions, now));
    }

    private static ProgressSessionDto ToSession(SessionInput input)
    {
        // Heaviest set; between equal weights, the one with more reps.
        var top = input.Sets.OrderByDescending(s => s.Weight).ThenByDescending(s => s.Reps).First();

        return new ProgressSessionDto(
            input.WorkoutId, input.WorkoutName, input.Date, input.Sets.ToList(),
            TopWeight: top.Weight,
            TopWeightReps: top.Reps,
            BestReps: input.Sets.Max(s => s.Reps),
            Volume: Math.Round(input.Sets.Sum(s => s.Weight * s.Reps), 2),
            EstimatedOneRepMax: input.Sets.Max(s => EstimateOneRepMax(s.Weight, s.Reps)));
    }

    /// <summary>Records go to the first session that reached them, so matching a record later doesn't move its date.</summary>
    private static ProgressRecordsDto? FindRecords(List<ProgressSessionDto> sessions)
    {
        if (sessions.Count == 0) return null;

        PersonalRecordDto? heaviest = null, oneRepMax = null, mostReps = null, volume = null;
        foreach (var session in sessions)
        {
            foreach (var set in session.Sets)
            {
                if (heaviest is null || set.Weight > heaviest.Weight || (set.Weight == heaviest.Weight && set.Reps > heaviest.Reps))
                    heaviest = new PersonalRecordDto(set.Weight, set.Weight, set.Reps, session.WorkoutId, session.Date);

                if (mostReps is null || set.Reps > mostReps.Reps || (set.Reps == mostReps.Reps && set.Weight > mostReps.Weight))
                    mostReps = new PersonalRecordDto(set.Reps, set.Weight, set.Reps, session.WorkoutId, session.Date);

                if (EstimateOneRepMax(set.Weight, set.Reps) is { } estimate && (oneRepMax is null || estimate > oneRepMax.Value))
                    oneRepMax = new PersonalRecordDto(estimate, set.Weight, set.Reps, session.WorkoutId, session.Date);
            }

            if (volume is null || session.Volume > volume.Value)
                volume = new PersonalRecordDto(session.Volume, null, null, session.WorkoutId, session.Date);
        }

        return new ProgressRecordsDto(heaviest!, oneRepMax, mostReps!, volume!);
    }

    /// <summary>
    /// Compares the latest session with where things stood a month, three months and a year before
    /// <paramref name="now"/> (the last session on or before that date), and with the very first session.
    /// </summary>
    private static List<ProgressChangeDto> FindChanges(List<ProgressSessionDto> sessions, DateTime now)
    {
        if (sessions.Count < 2) return [];

        var latest = sessions[^1];
        var earlier = sessions[..^1];
        var changes = new List<ProgressChangeDto>();

        void Add(string period, ProgressSessionDto? then)
        {
            if (then is null || changes.Any(c => c.WorkoutId == then.WorkoutId)) return;
            changes.Add(new ProgressChangeDto(
                period, then.WorkoutId, then.Date,
                then.EstimatedOneRepMax, latest.EstimatedOneRepMax,
                then.TopWeight, latest.TopWeight,
                then.BestReps, latest.BestReps));
        }

        foreach (var (period, days) in Periods)
            Add(period, earlier.LastOrDefault(s => s.Date <= now.AddDays(-days)));
        Add("start", earlier[0]);

        return changes.OrderBy(c => Array.IndexOf(PeriodOrder, c.Period)).ToList();
    }
}
