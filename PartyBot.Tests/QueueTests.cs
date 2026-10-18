using PartyBot.Api.Domain;
using PartyBot.Api.Infrastructure;
using PartyBot.Tests.Fakes;

namespace PartyBot.Tests;

public sealed class QueueTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-18T18:00:00Z");

    internal static TaskDefinition[] Catalog(int cheap = 20, int medium = 5)
    {
        return Enumerable.Range(1, cheap)
            .Select(i => new TaskDefinition { Id = $"a{i:00}", Text = $"A{i}", Points = 1 })
            .Concat(Enumerable.Range(1, medium)
                .Select(i => new TaskDefinition { Id = $"b{i:00}", Text = $"B{i}", Points = 2 }))
            .Append(new TaskDefinition { Id = "c01", Text = "C1", Points = 6, Repeatable = true }).ToArray();
    }

    private static Participant Create(TaskDefinition[]? catalog = null)
    {
        var participant = new Participant { Id = 1 };
        PersonalQueue.Initialize(participant, catalog ?? Catalog(), new KeepOrderRandom());
        return participant;
    }

    private static string[] Queue(Participant participant)
    {
        return PersonalQueue.Queued(participant).Select(task => task.TaskId).ToArray();
    }

    private static Attempt TakeAndSubmit(Participant participant)
    {
        var attempt = PersonalQueue.Take(participant, Now)!;
        Assert.True(PersonalQueue.MarkPending(participant, attempt.Id, Now));
        return attempt;
    }

    [Fact]
    public void Normative_example_refills_after_actual_bucket_and_skip_bypasses_target()
    {
        var participant = Create();
        Assert.Equal(["a01", "b01", "c01", "a02", "b02"], Queue(participant));
        Assert.Equal(6, participant.NextBucket);
        var first = PersonalQueue.Take(participant, Now)!;
        Assert.Equal("a01", first.TaskId);
        Assert.Equal(["b01", "c01", "a02", "b02", "a03"], Queue(participant));
        Assert.Equal(2, participant.NextBucket);
        Assert.True(PersonalQueue.Skip(participant, first.Id, Now));
        Assert.Equal(["b01", "c01", "a02", "b02", "a03", "a01"], Queue(participant));
        Assert.Equal(2, participant.NextBucket);
        Assert.False(PersonalQueue.Skip(participant, first.Id, Now));
        TakeAndSubmit(participant);
        Assert.Equal(5, Queue(participant).Length);
        Assert.Equal(2, participant.NextBucket);
        TakeAndSubmit(participant);
        Assert.Equal(["a02", "b02", "a03", "a01", "b03"], Queue(participant));
        Assert.Equal(6, participant.NextBucket);
    }

    [Fact]
    public void Empty_pools_stop_and_failed_search_does_not_move_cursor()
    {
        var participant = Create(Catalog(1, 1));
        Assert.Equal(3, Queue(participant).Length);
        var cursor = participant.NextBucket;
        for (var i = 0; i < 3; i++) TakeAndSubmit(participant);
        Assert.Null(PersonalQueue.Take(participant, Now));
        Assert.Empty(Queue(participant));
        Assert.Equal(cursor, participant.NextBucket);
        Assert.All(participant.Tasks, task => Assert.Equal(TaskLocation.Pending, task.Location));
        var empty = Create([]);
        PersonalQueue.Refill(empty);
        Assert.Null(PersonalQueue.Take(empty, Now));
    }

    [Fact]
    public void Seed_reproduces_independent_personal_pools()
    {
        var first = new Participant();
        var second = new Participant();
        var catalog = Catalog();
        PersonalQueue.Initialize(first, catalog, new SeededRandomSource(42));
        PersonalQueue.Initialize(second, catalog, new SeededRandomSource(42));
        Assert.Equal(first.Tasks.Select(task => (task.TaskId, task.Location, task.Position)),
            second.Tasks.Select(task => (task.TaskId, task.Location, task.Position)));
        var before = Queue(second);
        PersonalQueue.Take(first, Now);
        Assert.Equal(before, Queue(second));
    }

    [Fact]
    public void Several_rejections_can_exceed_six_and_preserve_return_order()
    {
        var participant = Create();
        var pending = Enumerable.Range(0, 3).Select(_ => TakeAndSubmit(participant)).ToArray();
        foreach (var attempt in pending) Assert.True(PersonalQueue.ResolvePending(participant, attempt.Id, false));
        Assert.Equal(8, Queue(participant).Length);
        Assert.Equal(pending.Select(attempt => attempt.TaskId), Queue(participant).TakeLast(3));
        Assert.False(PersonalQueue.ResolvePending(participant, pending[0].Id, false));
        Assert.Equal(8, Queue(participant).Length);
    }

    [Fact]
    public void Approved_repeatable_returns_to_bucket_tail_and_waits_for_refill()
    {
        var participant = Create();
        TakeAndSubmit(participant);
        TakeAndSubmit(participant);
        var repeatable = TakeAndSubmit(participant);
        var before = Queue(participant);
        var cursor = participant.NextBucket;
        Assert.Equal("c01", repeatable.TaskId);
        Assert.True(PersonalQueue.ResolvePending(participant, repeatable.Id, true));
        Assert.Equal(TaskLocation.Pool, participant.Tasks.Single(task => task.TaskId == "c01").Location);
        Assert.Equal(before, Queue(participant));
        Assert.Equal(cursor, participant.NextBucket);
        Assert.False(PersonalQueue.ResolvePending(participant, repeatable.Id, true));
    }

    [Fact]
    public void Approved_repeatable_can_refill_short_queue_using_normal_cursor()
    {
        var participant = Create(Catalog(0, 0));
        var attempt = TakeAndSubmit(participant);
        Assert.True(PersonalQueue.ResolvePending(participant, attempt.Id, true));
        Assert.Equal(["c01"], Queue(participant));
        var again = PersonalQueue.Take(participant, Now)!;
        Assert.NotEqual(attempt.Id, again.Id);
        Assert.Equal(AttemptState.Submitted, attempt.State);
    }

    [Fact]
    public void Approved_one_off_is_consumed_but_rejected_one_off_gets_new_attempt()
    {
        var participant = Create([new TaskDefinition { Id = "once", Text = "one", Points = 1 }]);
        var attempt = TakeAndSubmit(participant);
        Assert.True(PersonalQueue.ResolvePending(participant, attempt.Id, false));
        var again = TakeAndSubmit(participant);
        Assert.NotEqual(attempt.Id, again.Id);
        Assert.True(PersonalQueue.ResolvePending(participant, again.Id, true));
        Assert.Null(PersonalQueue.Take(participant, Now));
        Assert.Equal(TaskLocation.Consumed, Assert.Single(participant.Tasks).Location);
    }

    [Fact]
    public void Direct_return_precedes_refill_and_only_one_active_is_allowed()
    {
        var participant = Create(Catalog(1, 0));
        var attempt = PersonalQueue.Take(participant, Now)!;
        Assert.Throws<InvalidOperationException>(() => PersonalQueue.Take(participant, Now));
        Assert.True(PersonalQueue.Skip(participant, attempt.Id, Now));
        Assert.Equal(["c01", "a01"], Queue(participant));
    }

    [Fact]
    public void Repeatable_returns_to_tail_of_its_own_pool_without_reordering_other_tasks()
    {
        var catalog = Catalog(12);
        catalog[0].Repeatable = true;
        var participant = Create(catalog);
        var attempt = TakeAndSubmit(participant);
        var remainingPool = participant.Tasks.Where(t => t.Location == TaskLocation.Pool && t.Task.Points == 1)
            .OrderBy(t => t.Position).Select(t => t.TaskId).ToArray();
        Assert.True(PersonalQueue.ResolvePending(participant, attempt.Id, true));
        var returnedPool = participant.Tasks.Where(t => t.Location == TaskLocation.Pool && t.Task.Points == 1)
            .OrderBy(t => t.Position).Select(t => t.TaskId).ToArray();
        Assert.Equal(remainingPool.Append(attempt.TaskId), returnedPool);
    }

    private sealed class KeepOrderRandom : IRandomSource
    {
        public int Next(int exclusiveMax)
        {
            return exclusiveMax - 1;
        }
    }
}