namespace PartyBot.Api.Infrastructure;

public sealed class InstanceLease : IDisposable
{
    private readonly FileStream stream;
    public InstanceLease(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        try { stream = new FileStream(databasePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new InvalidOperationException("Database is already in use by another bot instance, or its lock file is unavailable."); }
    }
    public void Dispose() => stream.Dispose();
}
