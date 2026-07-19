namespace PalOps.Web.SaveGames;

internal static class SaveClock
{
    private static readonly TimeSpan BeijingOffset = TimeSpan.FromHours(8);

    public static DateTimeOffset BeijingNow() => DateTimeOffset.UtcNow.ToOffset(BeijingOffset);
}
