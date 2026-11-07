namespace Nerdy.Core.Subjects;

/// <summary>
/// How far along a subject or topic is. Only subtopics carry a progress value (0–100); a topic is the average of its
/// subtopics and a subject the average of all the subtopics in it, so a topic with many subtopics weighs more and a
/// topic without any doesn't count. Null when there is nothing to average yet.
/// </summary>
public static class StudyProgress
{
    public static int? Average(IEnumerable<int> subtopicProgress)
    {
        var values = subtopicProgress.ToList();
        return values.Count == 0 ? null : (int)Math.Round(values.Average(), MidpointRounding.AwayFromZero);
    }
}
