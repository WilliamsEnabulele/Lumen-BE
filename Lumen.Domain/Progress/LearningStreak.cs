namespace Lumen.Domain.Progress;

/// <summary>How many days in a row, and how much of this week.</summary>
/// <param name="Current">Days in an unbroken run ending today or yesterday. Zero once it breaks.</param>
/// <param name="Longest">The best run there has ever been, which survives breaking the current one.</param>
/// <param name="ThisWeek">Days taught on since Monday.</param>
/// <param name="Goal">How many days a week this is measured against.</param>
public sealed record Streak(int Current, int Longest, int ThisWeek, int Goal)
{
    public bool GoalMet => ThisWeek >= Goal;
}

/// <summary>
/// Turning up, counted.
///
/// This is deliberately the only number in the product that is shown as a number. It measures
/// <em>attendance</em> — whether somebody sat down and did some work — which is a plain fact
/// about a day. What they <em>know</em> is a belief estimated from a handful of answers, and
/// <see cref="Assessment"/>'s standing refuses to render that as a figure for exactly the
/// reason that applies here in reverse: a streak cannot be argued with, and a mastery
/// percentage can, endlessly, by the person it is about.
///
/// So the rule this file exists to hold: <b>a streak counts days, never concepts.</b> Nothing
/// here may take mastery as an input. A run of days that went badly is still a run of days.
/// </summary>
public static class LearningStreak
{
    /// <summary>
    /// Days a week the weekly goal asks for.
    ///
    /// Invented, like the price was. Five is a school week and nobody has tested whether it is
    /// the number that makes somebody come back or the number that makes them give up. It is a
    /// constant here so that changing it is one edit, and so that it is obvious it was chosen
    /// rather than derived.
    /// </summary>
    public const int DefaultWeeklyGoal = 5;

    /// <summary>
    /// Counts the run.
    ///
    /// Today not being in the set does not break anything: somebody who worked yesterday and
    /// has not started yet today still has their streak, because it is only lost by missing a
    /// whole day. Breaking it the moment midnight passes would mean the number is at its most
    /// discouraging exactly when they have not yet had a chance to keep it.
    /// </summary>
    public static Streak From(IEnumerable<DateOnly> days, DateOnly today, int goal = DefaultWeeklyGoal)
    {
        ArgumentNullException.ThrowIfNull(days);

        var taught = days.Distinct().OrderBy(day => day).ToArray();

        if (taught.Length == 0) return new Streak(0, 0, 0, goal);

        var longest = 1;
        var run = 1;

        for (var i = 1; i < taught.Length; i++)
        {
            run = taught[i] == taught[i - 1].AddDays(1) ? run + 1 : 1;
            longest = Math.Max(longest, run);
        }

        var last = taught[^1];
        // The run that is still alive is the one ending today or yesterday. Anything older is a
        // record, not a streak.
        var current = last == today || last == today.AddDays(-1) ? run : 0;

        // Monday, because a week that resets under somebody mid-Sunday-afternoon is a week that
        // takes work off them for no reason they can see.
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var thisWeek = taught.Count(day => day >= monday && day <= today);

        return new Streak(current, longest, thisWeek, goal);
    }
}
