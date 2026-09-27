using Lumen.Domain.Progress;

namespace Lumen.Tests;

/// <summary>
/// Counting days. The rule underneath every test here is that a streak counts attendance and
/// never mastery: a run of days that went badly is still a run of days.
/// </summary>
public class LearningStreakTests
{
    private static readonly DateOnly Wednesday = new(2026, 9, 23);

    private static DateOnly[] Days(params int[] agesInDays) =>
        agesInDays.Select(age => Wednesday.AddDays(-age)).ToArray();

    [Fact]
    public void Nobody_who_has_never_turned_up_has_a_streak()
    {
        var streak = LearningStreak.From([], Wednesday);

        Assert.Equal(0, streak.Current);
        Assert.Equal(0, streak.Longest);
        Assert.Equal(0, streak.ThisWeek);
    }

    [Fact]
    public void Consecutive_days_ending_today_are_the_current_run()
    {
        Assert.Equal(3, LearningStreak.From(Days(0, 1, 2), Wednesday).Current);
    }

    [Fact]
    public void Not_having_started_today_does_not_break_yesterdays_streak()
    {
        // Losing it at midnight puts the number at its most discouraging exactly when somebody
        // has not yet had the chance to keep it.
        Assert.Equal(3, LearningStreak.From(Days(1, 2, 3), Wednesday).Current);
    }

    [Fact]
    public void Missing_a_whole_day_breaks_it()
    {
        Assert.Equal(0, LearningStreak.From(Days(2, 3, 4), Wednesday).Current);
    }

    [Fact]
    public void The_longest_run_survives_breaking_the_current_one()
    {
        // Four in a row a fortnight ago, then one yesterday.
        var streak = LearningStreak.From(Days(1, 10, 11, 12, 13), Wednesday);

        Assert.Equal(1, streak.Current);
        Assert.Equal(4, streak.Longest);
    }

    [Fact]
    public void Twice_in_one_day_is_one_day()
    {
        var twice = new[] { Wednesday, Wednesday, Wednesday.AddDays(-1) };

        Assert.Equal(2, LearningStreak.From(twice, Wednesday).Current);
    }

    [Fact]
    public void The_week_is_counted_from_monday()
    {
        // Wednesday the 23rd; Monday the 21st. The Sunday before is last week.
        var streak = LearningStreak.From(Days(0, 1, 2, 3), Wednesday);

        Assert.Equal(3, streak.ThisWeek);
    }

    [Fact]
    public void A_week_that_starts_on_monday_counts_monday()
    {
        var monday = new DateOnly(2026, 9, 21);

        Assert.Equal(1, LearningStreak.From([monday], monday).ThisWeek);
    }

    [Fact]
    public void The_goal_is_met_when_the_week_reaches_it()
    {
        Assert.False(LearningStreak.From(Days(0, 1), Wednesday, goal: 5).GoalMet);
        Assert.True(LearningStreak.From(Days(0, 1, 2), Wednesday, goal: 3).GoalMet);
    }

    [Fact]
    public void Days_arriving_out_of_order_count_the_same()
    {
        var jumbled = new[] { Wednesday.AddDays(-2), Wednesday, Wednesday.AddDays(-1) };

        Assert.Equal(3, LearningStreak.From(jumbled, Wednesday).Current);
    }
}
