using YTdownloadBackend.Models.YTdownloadBackend.Models;

namespace YTdownloadBackend.Models;

public class Playlist
{
    public int Id { get; set; }
    public string PlaylistId { get; set; } = default!;   // e.g. PL123...
    public int UserId { get; set; }
    public User? User { get; set; }   // Navigation property
    public string? PlaylistTitle { get; set;}

    public List<PlaylistSong> PlaylistSongs { get; set; } = new();

}
