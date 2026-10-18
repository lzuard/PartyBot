using PartyBot.Api.Infrastructure;

namespace PartyBot.Api.Domain;

public static class PersonalQueue
{
    public const int TargetLength = 5;

    public static void Initialize(Participant participant, IReadOnlyList<TaskDefinition> catalog, IRandomSource random)
    {
        if (participant.Tasks.Count != 0) throw new InvalidOperationException("Pools already initialized.");
        foreach (var bucket in catalog.GroupBy(task => task.Points).OrderBy(group => group.Key))
        {
            var tasks = bucket.OrderBy(task => task.Id, StringComparer.Ordinal).ToArray();
            for (var i = tasks.Length - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                (tasks[i], tasks[j]) = (tasks[j], tasks[i]);
            }

            foreach (var task in tasks)
                participant.Tasks.Add(new TaskAvailability
                {
                    Participant = participant, ParticipantId = participant.Id, Task = task, TaskId = task.Id,
                    Location = TaskLocation.Pool, Position = participant.NextPosition++
                });
        }

        participant.NextBucket = catalog.Select(task => task.Points).DefaultIfEmpty(0).Min();
        Refill(participant);
    }

    public static TaskAvailability[] Queued(Participant participant)
    {
        return participant.Tasks
            .Where(task => task.Location == TaskLocation.Queue).OrderBy(task => task.Position).ToArray();
    }

    public static void Refill(Participant participant)
    {
        var buckets = participant.Tasks.Select(task => task.Task.Points).Distinct().Order().ToArray();
        if (buckets.Length == 0) return;
        var start = Array.IndexOf(buckets, participant.NextBucket);
        if (start < 0) throw new InvalidOperationException("Unknown queue cursor.");
        for (var count = Queued(participant).Length; count < TargetLength; count++)
        {
            TaskAvailability? selected = null;
            var selectedIndex = start;
            for (var offset = 0; offset < buckets.Length; offset++)
            {
                selectedIndex = (start + offset) % buckets.Length;
                selected = participant.Tasks.Where(task =>
                        task.Location == TaskLocation.Pool && task.Task.Points == buckets[selectedIndex])
                    .OrderBy(task => task.Position).FirstOrDefault();
                if (selected is not null) break;
            }

            if (selected is null) return;
            Move(participant, selected, TaskLocation.Queue);
            start = (selectedIndex + 1) % buckets.Length;
            participant.NextBucket = buckets[start];
        }
    }

    public static Attempt? Take(Participant participant, DateTimeOffset now)
    {
        if (participant.Tasks.Any(task => task.Location == TaskLocation.Active))
            throw new InvalidOperationException("An active attempt already exists.");
        Refill(participant);
        var head = Queued(participant).FirstOrDefault();
        if (head is null) return null;
        var attempt = new Attempt { ParticipantId = participant.Id, TaskId = head.TaskId, TakenAt = now };
        head.Location = TaskLocation.Active;
        head.CurrentAttempt = attempt;
        head.CurrentAttemptId = attempt.Id;
        Refill(participant);
        return attempt;
    }

    public static bool Skip(Participant participant, Guid attemptId, DateTimeOffset now)
    {
        var active = participant.Tasks.SingleOrDefault(task =>
            task.Location == TaskLocation.Active && task.CurrentAttemptId == attemptId);
        if (active is null) return false;
        active.CurrentAttempt!.State = AttemptState.Skipped;
        active.CurrentAttempt.EndedAt = now;
        Return(participant, active, TaskLocation.Queue);
        return true;
    }

    // Queue primitives for stage 3. No moderation or scoring workflow is exposed here.
    public static bool MarkPending(Participant participant, Guid attemptId, DateTimeOffset now)
    {
        var active = participant.Tasks.SingleOrDefault(task =>
            task.Location == TaskLocation.Active && task.CurrentAttemptId == attemptId);
        if (active is null) return false;
        active.CurrentAttempt!.State = AttemptState.Submitted;
        active.CurrentAttempt.EndedAt = now;
        active.Location = TaskLocation.Pending;
        Refill(participant);
        return true;
    }

    public static bool ResolvePending(Participant participant, Guid attemptId, bool approved)
    {
        var pending = participant.Tasks.SingleOrDefault(task =>
            task.Location == TaskLocation.Pending && task.CurrentAttemptId == attemptId);
        if (pending is null) return false;
        Return(participant, pending,
            !approved ? TaskLocation.Queue : pending.Task.Repeatable ? TaskLocation.Pool : TaskLocation.Consumed);
        return true;
    }

    private static void Return(Participant participant, TaskAvailability task, TaskLocation location)
    {
        task.CurrentAttempt = null;
        task.CurrentAttemptId = null;
        Move(participant, task, location);
        Refill(participant);
    }

    private static void Move(Participant participant, TaskAvailability task, TaskLocation location)
    {
        task.Location = location;
        task.Position = checked(participant.NextPosition++);
    }
}