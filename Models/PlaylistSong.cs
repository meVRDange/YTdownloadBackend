namespace YTdownloadBackend.Models
{
    using System;

    namespace YTdownloadBackend.Models
    {
        public class PlaylistSong
        {
            public int Id { get; set; }

            // FK to your existing Playlist table (Playlist.Id)
            public required string PlaylistId { get; set; }

            // YouTube video id (watch?v=VIDEOID)
            public string VideoId { get; set; } = default!;

            // optional metadata
            public string? Title { get; set; }
            public long? DurationSeconds { get; set; }   // optional
            public string? ThumbnailUrl { get; set; }    // optional

            // status tracking
            public string Status { get; set; } = "pending"; // pending, downloaded, failed
            public int RetryCount { get; set; } = 0;
            public DateTime? DownloadedAt { get; set; }
            public DateTime LastChecked { get; set; } = DateTime.UtcNow;
        }
    }

}
