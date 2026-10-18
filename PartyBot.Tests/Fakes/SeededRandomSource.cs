using PartyBot.Api.Infrastructure;

namespace PartyBot.Tests.Fakes;

public sealed class SeededRandomSource(int seed) : IRandomSource
{
    private readonly Random random = new(seed);

    public int Next(int exclusiveMax)
    {
        return random.Next(exclusiveMax);
    }
}