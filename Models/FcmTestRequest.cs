namespace YTdownloadBackend.Models;

public class FcmTestRequest
{
    /// <summary>
    /// Song title to show in the test notification. Defaults to "Test Song" if empty.
    /// </summary>
    public string? SongTitle { get; set; }

    /// <summary>
    /// Download URL to include in the test notification. Defaults to a placeholder if empty.
    /// </summary>
    public string? DownloadUrl { get; set; }
}
