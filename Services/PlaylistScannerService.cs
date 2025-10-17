using Microsoft.EntityFrameworkCore;
using YTdownloadBackend.Data;
using YTdownloadBackend.Models.YTdownloadBackend.Models;

namespace YTdownloadBackend.Services
{
    public class PlaylistScannerService
    {
        private readonly AppDbContext _context;
        private readonly IYouTubeService _youTube;
        private readonly IYtDlpService _downloader;

        public PlaylistScannerService(AppDbContext context, IYouTubeService youTube, IYtDlpService downloader)
        {
            _context = context;
            _youTube = youTube;
            _downloader = downloader;
        }

        public async Task ScanForNewAsync(string playlistId, string userName)
        {
            var playlist = await _context.Playlists.FirstOrDefaultAsync(p => p.PlaylistId == playlistId);
            if (playlist == null)
            {
                Console.WriteLine("Playlist not found in DB");
                return;
            }

            List<PlaylistSong>? playlistSongs = await _youTube.GetPlaylistVideosAsync(playlistId);
            if (playlistSongs == null)
            {
                Console.WriteLine("The playlist does not Have any songs");
                return;
            }

            List<string> existingVideoIds = await _context.PlaylistSongs
                .Where(ps => ps.PlaylistId == playlistId)
                .Select(ps => ps.VideoId)
                .ToListAsync();

            List<PlaylistSong>? missing = playlistSongs
                .Where(v => !existingVideoIds.Contains(v.VideoId))
                .ToList();

            Console.WriteLine($"Found {missing.Count} new songs.");

            foreach (var song in missing)
            {
                Console.WriteLine($"Downloading {song.Title}...");
                bool ok = await _downloader.DownloadAudioAsync(song.VideoId);

                
                if (ok)
                {
                    _context.PlaylistSongs.Add(song);
                    await _context.SaveChangesAsync();
                    Console.WriteLine($"✅ Downloaded: {song.Title}");
                }
                else
                {
                    Console.WriteLine($"❌ Failed: {song.Title}");
                }
            }
        }
    }
}
