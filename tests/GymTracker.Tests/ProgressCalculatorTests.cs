using GymTracker.Api;

namespace GymTracker.Tests;

/// <summary>Pure unit tests for the progress maths: estimated 1RM, session figures, records and comparisons.</summary>
public class ProgressCalculatorTests
{
    private static readonly DateTime Now = new(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);
    private static int _nextId = 1;

    private static ProgressCalculator.SessionInput Session(int daysAgo, params (double Weight, int Reps)[] sets) =>
        new(_nextId++, "Push", Now.AddDays(-daysAgo), sets.Select((s, i) => new SetDto(i + 1, s.Weight, s.Reps)).ToList());

    private static ExerciseProgressDto Build(params ProgressCalculator.SessionInput[] sessions) =>
        ProgressCalculator.Build(7, "Bench Press", false, sessions, Now);

    // ----- Estimated one-rep max -----

    [Theory]
    [InlineData(100, 1, 100)]      // a single is its own max
    [InlineData(100, 2, 106.7)]
    [InlineData(100, 5, 116.7)]
    [InlineData(60, 10, 80)]
    [InlineData(62.5, 6, 75)]
    [InlineData(80, 12, 112)]      // the highest rep count still used
    [InlineData(2.5, 3, 2.8)]      // rounded to one decimal place
    public void EstimateOneRepMax_uses_the_Epley_formula(double weight, int reps, double expected)
    {
        Assert.Equal(expected, ProgressCalculator.EstimateOneRepMax(weight, reps));
    }

    [Theory]
    [InlineData(0, 10)]     // bodyweight: nothing to scale
    [InlineData(-5, 5)]
    [InlineData(60, 13)]    // too many reps for the formula to mean much
    [InlineData(60, 50)]
    [InlineData(60, 0)]
    [InlineData(60, -1)]
    public void EstimateOneRepMax_is_null_when_it_cannot_be_estimated(double weight, int reps)
    {
        Assert.Null(ProgressCalculator.EstimateOneRepMax(weight, reps));
    }

    [Fact]
    public void EstimateOneRepMax_goes_up_with_more_weight_or_more_reps()
    {
        Assert.True(ProgressCalculator.EstimateOneRepMax(100, 6) > ProgressCalculator.EstimateOneRepMax(100, 5));
        Assert.True(ProgressCalculator.EstimateOneRepMax(102.5, 5) > ProgressCalculator.EstimateOneRepMax(100, 5));
    }

    // ----- Building -----

    [Fact]
    public void Build_with_no_sessions_has_no_records_or_changes()
    {
        var progress = Build();

        Assert.Equal(7, progress.ExerciseId);
        Assert.Equal("Bench Press", progress.Name);
        Assert.Empty(progress.Sessions);
        Assert.Null(progress.Records);
        Assert.Empty(progress.Changes);
    }

    [Fact]
    public void Build_passes_through_the_archived_flag()
    {
        Assert.True(ProgressCalculator.Build(1, "Dips", true, [], Now).IsArchived);
    }

    [Fact]
    public void Build_orders_sessions_oldest_first_whatever_order_they_arrive_in()
    {
        var progress = Build(Session(1, (70, 5)), Session(30, (60, 5)), Session(10, (65, 5)));

        Assert.Equal([60, 65, 70], progress.Sessions.Select(s => s.TopWeight));
        Assert.True(progress.Sessions[0].Date < progress.Sessions[1].Date);
    }

    [Fact]
    public void Build_ignores_sessions_without_sets()
    {
        var progress = Build(Session(5), Session(1, (60, 5)));

        Assert.Single(progress.Sessions);
    }

    [Fact]
    public void Session_figures_are_derived_from_its_sets()
    {
        var input = Session(1, (60, 10), (80, 5), (80, 3), (40, 15));

        var session = Build(input).Sessions.Single();

        Assert.Equal(input.WorkoutId, session.WorkoutId);
        Assert.Equal("Push", session.WorkoutName);
        Assert.Equal(input.Date, session.Date);
        Assert.Equal(4, session.Sets.Count);
        Assert.Equal(80, session.TopWeight);
        Assert.Equal(5, session.TopWeightReps);          // the better of the two sets at 80
        Assert.Equal(15, session.BestReps);
        Assert.Equal(600 + 400 + 240 + 600, session.Volume);
        Assert.Equal(93.3, session.EstimatedOneRepMax);  // 80 × 5; the 15-rep set is not used
    }

    [Fact]
    public void Session_one_rep_max_can_come_from_a_lighter_set_with_more_reps()
    {
        var session = Build(Session(1, (100, 1), (90, 8))).Sessions.Single();

        Assert.Equal(100, session.TopWeight);
        Assert.Equal(114, session.EstimatedOneRepMax); // 90 × 8 beats the single at 100
    }

    [Fact]
    public void Session_has_no_one_rep_max_for_bodyweight_or_high_rep_work()
    {
        Assert.Null(Build(Session(1, (0, 12), (0, 10))).Sessions.Single().EstimatedOneRepMax);
        Assert.Null(Build(Session(1, (20, 20), (20, 15))).Sessions.Single().EstimatedOneRepMax);
    }

    [Fact]
    public void Session_volume_is_rounded_to_avoid_floating_point_noise()
    {
        var session = Build(Session(1, (0.1, 3), (0.2, 3))).Sessions.Single();

        Assert.Equal(0.9, session.Volume);
    }

    // ----- Records -----

    [Fact]
    public void Records_pick_the_best_of_each_kind_across_all_sessions()
    {
        var heavy = Session(40, (100, 2), (90, 5));
        var reps = Session(20, (50, 20), (85, 8));
        var volume = Session(5, (70, 10), (70, 10), (70, 10), (70, 10));

        var records = Build(heavy, reps, volume).Records!;

        Assert.Equal(new PersonalRecordDto(100, 100, 2, heavy.WorkoutId, heavy.Date), records.HeaviestSet);
        Assert.Equal(new PersonalRecordDto(107.7, 85, 8, reps.WorkoutId, reps.Date), records.BestOneRepMax);
        Assert.Equal(new PersonalRecordDto(20, 50, 20, reps.WorkoutId, reps.Date), records.MostReps);
        Assert.Equal(new PersonalRecordDto(2800, null, null, volume.WorkoutId, volume.Date), records.BestVolume);
    }

    [Fact]
    public void Records_stay_with_the_first_session_that_set_them()
    {
        var first = Session(30, (100, 5));
        var repeat = Session(1, (100, 5));

        var records = Build(first, repeat).Records!;

        Assert.Equal(first.WorkoutId, records.HeaviestSet.WorkoutId);
        Assert.Equal(first.WorkoutId, records.BestOneRepMax!.WorkoutId);
        Assert.Equal(first.WorkoutId, records.MostReps.WorkoutId);
        Assert.Equal(first.WorkoutId, records.BestVolume.WorkoutId);
    }

    [Fact]
    public void Heaviest_set_prefers_more_reps_at_the_same_weight()
    {
        var first = Session(30, (100, 3));
        var better = Session(1, (100, 5));

        var heaviest = Build(first, better).Records!.HeaviestSet;

        Assert.Equal(better.WorkoutId, heaviest.WorkoutId);
        Assert.Equal(5, heaviest.Reps);
    }

    [Fact]
    public void Most_reps_prefers_more_weight_at_the_same_reps()
    {
        var first = Session(30, (50, 12));
        var better = Session(1, (60, 12));

        var mostReps = Build(first, better).Records!.MostReps;

        Assert.Equal(better.WorkoutId, mostReps.WorkoutId);
        Assert.Equal(60, mostReps.Weight);
    }

    [Fact]
    public void Bodyweight_exercises_have_rep_records_but_no_one_rep_max_record()
    {
        var records = Build(Session(10, (0, 8)), Session(1, (0, 12), (0, 10))).Records!;

        Assert.Null(records.BestOneRepMax);
        Assert.Equal(12, records.MostReps.Value);
        Assert.Equal(0, records.HeaviestSet.Value);
        Assert.Equal(12, records.HeaviestSet.Reps);
        Assert.Equal(0, records.BestVolume.Value);
    }

    [Fact]
    public void A_single_session_holds_every_record()
    {
        var only = Session(1, (60, 8));

        var records = Build(only).Records!;

        Assert.All(
            new[] { records.HeaviestSet, records.BestOneRepMax!, records.MostReps, records.BestVolume },
            r => Assert.Equal(only.WorkoutId, r.WorkoutId));
    }

    // ----- Then vs now -----

    [Fact]
    public void Changes_need_at_least_two_sessions()
    {
        Assert.Empty(Build(Session(400, (60, 5))).Changes);
    }

    [Fact]
    public void Changes_compare_the_latest_session_with_a_month_three_months_and_a_year_ago_and_the_start()
    {
        var first = Session(500, (40, 5));
        var yearAgo = Session(380, (50, 5));
        var quarterAgo = Session(100, (60, 5));
        var monthAgo = Session(35, (65, 5));
        var recent = Session(10, (67.5, 5));
        var latest = Session(1, (70, 5));

        var changes = Build(first, yearAgo, quarterAgo, monthAgo, recent, latest).Changes;

        Assert.Equal(["month", "threeMonths", "year", "start"], changes.Select(c => c.Period));
        Assert.Equal([monthAgo.WorkoutId, quarterAgo.WorkoutId, yearAgo.WorkoutId, first.WorkoutId], changes.Select(c => c.WorkoutId));
        Assert.Equal([65, 60, 50, 40], changes.Select(c => c.TopWeightThen));
        Assert.All(changes, c => Assert.Equal(70, c.TopWeightNow));
        Assert.All(changes, c => Assert.Equal(81.7, c.OneRepMaxNow));
    }

    [Fact]
    public void A_change_carries_both_sides_of_every_measure()
    {
        var then = Session(400, (80, 5), (60, 12));
        var now = Session(2, (100, 3), (70, 15));

        var change = Build(then, now).Changes.Single();

        Assert.Equal("year", change.Period);
        Assert.Equal(then.WorkoutId, change.WorkoutId);
        Assert.Equal(then.Date, change.Date);
        Assert.Equal(93.3, change.OneRepMaxThen);
        Assert.Equal(110, change.OneRepMaxNow);
        Assert.Equal(80, change.TopWeightThen);
        Assert.Equal(100, change.TopWeightNow);
        Assert.Equal(12, change.BestRepsThen);
        Assert.Equal(15, change.BestRepsNow);
    }

    [Fact]
    public void A_period_uses_the_last_session_on_or_before_its_cutoff()
    {
        var tooOld = Session(60, (50, 5));
        var onTheDay = Session(30, (55, 5));
        var tooNew = Session(29, (60, 5));
        var latest = Session(0, (65, 5));

        var month = Build(tooOld, onTheDay, tooNew, latest).Changes.Single(c => c.Period == "month");

        Assert.Equal(onTheDay.WorkoutId, month.WorkoutId);
    }

    [Fact]
    public void Periods_without_a_session_that_old_are_left_out()
    {
        var first = Session(45, (60, 5));
        var latest = Session(1, (65, 5));

        var changes = Build(first, latest).Changes;

        // 45 days of history: no 3-month or year comparison, and "start" would repeat the month one.
        Assert.Equal(["month"], changes.Select(c => c.Period));
    }

    [Fact]
    public void When_periods_share_a_session_only_the_longest_is_reported()
    {
        var old = Session(400, (60, 5));
        var latest = Session(1, (80, 5));

        var changes = Build(old, latest).Changes;

        Assert.Equal(["year"], changes.Select(c => c.Period));
    }

    [Fact]
    public void With_only_recent_history_the_comparison_is_with_the_first_session()
    {
        var first = Session(12, (60, 5));
        var latest = Session(1, (62.5, 5));

        var change = Build(first, Session(6, (60, 6)), latest).Changes.Single();

        Assert.Equal("start", change.Period);
        Assert.Equal(first.WorkoutId, change.WorkoutId);
    }

    [Fact]
    public void The_latest_session_is_never_compared_with_itself()
    {
        // The only session older than a month is also the latest one trained.
        var first = Session(90, (60, 5));
        var latest = Session(40, (70, 5));

        var changes = Build(first, latest).Changes;

        Assert.DoesNotContain(changes, c => c.WorkoutId == latest.WorkoutId);
        Assert.Equal(["threeMonths"], changes.Select(c => c.Period));
    }

    [Fact]
    public void Changes_show_a_decline_as_well_as_an_improvement()
    {
        var change = Build(Session(400, (100, 5)), Session(1, (80, 5))).Changes.Single();

        Assert.True(change.OneRepMaxNow < change.OneRepMaxThen);
        Assert.True(change.TopWeightNow < change.TopWeightThen);
    }

    [Fact]
    public void Changes_have_no_one_rep_max_when_a_side_cannot_be_estimated()
    {
        var change = Build(Session(400, (0, 8)), Session(1, (0, 15))).Changes.Single();

        Assert.Null(change.OneRepMaxThen);
        Assert.Null(change.OneRepMaxNow);
        Assert.Equal(8, change.BestRepsThen);
        Assert.Equal(15, change.BestRepsNow);
    }
}
