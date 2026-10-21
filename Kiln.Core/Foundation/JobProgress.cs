namespace Kiln.Core;

public readonly record struct JobProgress(int Count, double Fraction)
{
    public static JobProgress From(IEnumerable<Job> jobs)
    {
        var running = jobs.Where(j => j.State == Job.JobState.Running).ToArray();
        return new(running.Length, running.Length == 0 ? 0 : running.Average(j => double.IsFinite(j.Fraction) ? Math.Clamp(j.Fraction, 0, 1) : 0));
    }
}
