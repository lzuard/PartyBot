namespace PartyBot.Api.Infrastructure;

public interface IRandomSource
{
    int Next(int exclusiveMax);
}

public sealed class SystemRandomSource : IRandomSource
{
    public int Next(int exclusiveMax) => Random.Shared.Next(exclusiveMax);
}
