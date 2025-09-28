namespace YTdownloadBackend.Models;

public class Playlist
{
    public int Id { get; set; }
    public string PlaylistId { get; set; } = default!;   // e.g. PL123...
    public int UserId { get; set; }
    public User? User { get; set; }   // Navigation property
    public string? PlaylistName { get; set;}
}
