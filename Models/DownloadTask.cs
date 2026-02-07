namespace YTdownloadBackend.Models.YTdownloadBackend.Models
{
    public class DownloadTask
    {
        public int Id { get; set; }

        // FK to PlaylistSong
        public int PlaylistSongId { get; set; }
        public PlaylistSong? PlaylistSong { get; set; }

        // Video id for convenience
        public string VideoId { get; set; } = default!;

        // pending, processing, completed, failed
        public string Status { get; set; } = "pending";
        public int RetryCount { get; set; } = 0;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastChecked { get; set; } = DateTime.UtcNow;
        public string? LastError { get; set; }
    }
}
