using System.Collections.Concurrent;

namespace Lumen.Infrastructure.Storage;

/// <summary>One student, and every day they did some work.</summary>
public sealed record StudyDays(Guid StudentId, HashSet<DateOnly> Days);

/// <summary>
/// Which days a student turned up.
///
/// A set of dates rather than a row per session, because that is the whole question: a streak
/// asks whether the day happened, not how much of it there was. Counting sessions would make
/// two short sittings worth more than one long one, which is not a claim anybody wants to
/// defend.
///
/// Known limitation, stated rather than hidden: the day is the server's UTC date. A student
/// well east or west of UTC can have a session land on the wrong side of their own midnight
/// and see a streak break they did not earn. Fixing it means the client telling the server
/// which day it is where they are, and trusting it, which is a decision rather than an
/// oversight.
/// </summary>
public interface IStudyDayStore
{
    /// <summary>Notes that today happened. Doing it twice in a day is doing it once.</summary>
    void Record(Guid studentId, DateOnly day);

    IReadOnlyCollection<DateOnly> DaysFor(Guid studentId);
}

public sealed class InMemoryStudyDayStore : IStudyDayStore
{
    private readonly ConcurrentDictionary<Guid, HashSet<DateOnly>> _days = new();

    public void Record(Guid studentId, DateOnly day)
    {
        var days = _days.GetOrAdd(studentId, _ => []);
        lock (days) days.Add(day);
    }

    public IReadOnlyCollection<DateOnly> DaysFor(Guid studentId)
    {
        if (!_days.TryGetValue(studentId, out var days)) return [];
        lock (days) return days.ToArray();
    }
}

public sealed class FileStudyDayStore : IStudyDayStore
{
    private readonly ConcurrentDictionary<Guid, HashSet<DateOnly>> _days = new();
    private readonly string _root;

    public FileStudyDayStore(string root)
    {
        _root = Path.Combine(root, "days");
        Directory.CreateDirectory(_root);

        foreach (var record in JsonFiles.ReadAll<StudyDays>(_root)) _days[record.StudentId] = record.Days;
    }

    public void Record(Guid studentId, DateOnly day)
    {
        var days = _days.GetOrAdd(studentId, _ => []);

        lock (days)
        {
            // Written only when the day is new. A turn happens every few seconds, and rewriting
            // the same file on each one would make a lesson a stream of identical disk writes.
            if (!days.Add(day)) return;

            JsonFiles.Write(Path.Combine(_root, $"{studentId}.json"), new StudyDays(studentId, days));
        }
    }

    public IReadOnlyCollection<DateOnly> DaysFor(Guid studentId)
    {
        if (!_days.TryGetValue(studentId, out var days)) return [];
        lock (days) return days.ToArray();
    }
}
