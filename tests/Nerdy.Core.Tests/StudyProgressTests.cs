using Nerdy.Core.Subjects;

namespace Nerdy.Core.Tests;

public class StudyProgressTests
{
    [Fact]
    public void Nothing_to_average_is_no_progress_rather_than_zero() => Assert.Null(StudyProgress.Average([]));

    [Fact]
    public void Every_subtopic_counts_equally() => Assert.Equal(50, StudyProgress.Average([60, 20, 70]));

    [Fact]
    public void Halves_round_up() => Assert.Equal(13, StudyProgress.Average([25, 0]));
}
