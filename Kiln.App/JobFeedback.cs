using Kiln.Core;

namespace Kiln.App;

internal sealed record JobFeedback(string Hint, HintKind Kind, string Title, string Body)
{
    internal static JobFeedback? From(Job job)
    {
        if (job.Silent) return null;
        return job.State switch
        {
            Job.JobState.Success => new(Localization.T($"{job.Title} 已完成", $"{job.Title} finished"), HintKind.Success, Localization.T("已完成", "Finished"), job.Title),
            Job.JobState.Failed => new(Localization.T($"{job.Title} 失败：{job.Error}", $"{job.Title} failed: {job.Error}"), HintKind.Error, Localization.T($"{job.Title} 失败", $"{job.Title} failed"), job.Error ?? ""),
            _ => null,
        };
    }
}
