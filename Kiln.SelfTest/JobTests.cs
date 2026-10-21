using Kiln.Core;

internal static class JobTests
{
    internal static void Run(Action<string, bool> check)
    {
        var job = new Job("State fixture"); var states = new List<Job.JobState>(); var observedError = ""; var unlocked = true;
        job.StateChanged += (sender, state) => { states.Add(state); unlocked &= Task.Run(() => sender.State).Wait(TimeSpan.FromSeconds(1)); observedError = sender.Error; };
        job.Fail("Synthetic failure"); job.State = Job.JobState.Failed;
        check("Job completion event is emitted once for the same state", states.SequenceEqual(new[] { Job.JobState.Failed }));
        check("Job failure reason is available to completion observers", observedError == "Synthetic failure");
        check("Job observers run outside its state lock", unlocked);
        var cancel = new Job("Cancel fixture"); var cancellationUnlocked = false;
        using (cancel.Token.Register(() => cancellationUnlocked = Task.Run(() => cancel.State).Wait(TimeSpan.FromSeconds(1)))) cancel.Cancel();
        check("Cancellation callbacks run outside the job lock", cancellationUnlocked && cancel.Token.IsCancellationRequested);
        var first = new Job("First") { FileFraction = .2 }; var second = new Job("Second") { FileFraction = .8 }; var finished = new Job("Finished") { State = Job.JobState.Success };
        check("Concurrent task progress uses the mean of running jobs", JobProgress.From(new[] { first, second, finished }) == new JobProgress(2, .5));
        first.State = Job.JobState.Cancelled; second.State = Job.JobState.Success;
        check("Taskbar progress clears after all jobs finish", JobProgress.From(new[] { first, second, finished }) == default);
        check("Invalid progress does not corrupt the taskbar", JobProgress.From(new[] { new Job("NaN") { FileFraction = double.NaN }, new Job("Negative") { FileFraction = -10 } }) == new JobProgress(2, 0));
        check("Job preserves the original silent option", new Job("Silent", silent: true).Silent && !new Job("Ordinary").Silent);
    }
}
