namespace YTdownloadBackend.Models
{
    using System;

    
    
        public class PlaylistSong
        {
            public int Id { get; set; }

            // FK to your existing Playlist table (Playlist.Id)
            public required string PlaylistId { get; set; }

            // YouTube video id (watch?v=VIDEOID)
            public string VideoId { get; set; } = default!;

            // optional metadata
            public required string Title { get; set; }
            public long? DurationSeconds { get; set; }   // optional
            public string? ThumbnailUrl { get; set; }    // optional

            // status tracking
            public PlaylistSongStatus Status { get; set; } = PlaylistSongStatus.Pending; // pending, completed, failed
            public int RetryCount { get; set; } = 0;
            public DateTime? DownloadedAt { get; set; }
            public DateTime LastChecked { get; set; } = DateTime.UtcNow;

            // Firebase Storage information
            public string? FirebaseStoragePath { get; set; }  // e.g., "users/123/songs/song-title.mp3"
            public string? FirebaseDownloadUrl { get; set; }  // Direct download URL
            public DateTime? DownloadUrlExpiry { get; set; }  // When the download URL was generated
        }
    
        public enum PlaylistSongStatus
        {
            Pending = 1,
            Processing = 3,
            Completed = 4,
            Failed = 5
    }

}
