using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using YTdownloadBackend.Models;
using YTdownloadBackend.Models.YTdownloadBackend.Models;

namespace YTdownloadBackend.Services
{
    public interface IYouTubeService
    {
        Task<string?> GetPlaylistTitleAsync(string playlistId);
        Task<List<PlaylistSong>?> GetPlaylistVideosAsync(string playlistId);
    }

    public class YouTubeService : IYouTubeService
    {
        private readonly HttpClient _http;
        private readonly string _apiKey;

        public YouTubeService(HttpClient httpClient, IConfiguration config)
        {
            _http = httpClient;
            //  Use environment variable first, fallback to config (for dev)
            _apiKey = Environment.GetEnvironmentVariable("YOUTUBE_API_KEY")
                      ?? config["YouTube:ApiKey"]
                      ?? throw new InvalidOperationException("YouTube API key not configured");
        }

        public async Task<string?> GetPlaylistTitleAsync(string playlistId)
        {
            var url = $"https://www.googleapis.com/youtube/v3/playlists" +
                      $"?part=snippet&id={playlistId}&key={_apiKey}";

            try
            {
                var resp = await _http.GetFromJsonAsync<YouTubeTitlePlaylistResponse>(url);
                return resp?.items?.FirstOrDefault()?.snippet?.title;
            }
            catch (Exception ex)
            {
                // Optionally log the error with ILogger
                Console.WriteLine($"YouTube API error: {ex.Message}");
                return null;
            }
        }

        public async Task<List<PlaylistSong>?> GetPlaylistVideosAsync(string playlistId)
        {
            var url = $"https://www.googleapis.com/youtube/v3/playlistItems" +
                      $"?part=snippet&playlistId={playlistId}&key={_apiKey}";

            try
            {
                var resp = await _http.GetFromJsonAsync<YouTubelistSongPlaylistResponse>(url);
                if (resp?.items == null)
                    return null;

                // Map YouTubePlaylistItem to YouTubeVideo
                List<PlaylistSong> videos = (
                    resp.items.Select(
                        item => new PlaylistSong
                        {
                            PlaylistId = playlistId,
                            VideoId = item.snippet.resourceId.videoId,
                            Title = item.snippet.title,
                            ThumbnailUrl = item.ToString().Contains("high") ? item.snippet.thumbnails.high.url : null

                        })).ToList();

                return videos;
            }
            catch (Exception ex)
            {
                // Optionally log the error with ILogger
                Console.WriteLine($"YouTube API error: {ex.Message}");
                return null;
            }
        }

        // DTOs for JSON mapping
        private record YouTubeTitlePlaylistResponse(List<YouTubeTitlePlaylistItem> items);
        private record YouTubeTitlePlaylistItem(YouTubeTitleSnippet snippet);
        private record YouTubeTitleSnippet(string title);


        private record YouTubelistSongPlaylistResponse(List<YouTubelistSongPlaylistItem> items);
        private record YouTubelistSongPlaylistItem(YouTubelistSongSnippet snippet);
        private record YouTubelistSongSnippet(string title, YouTubelistSongResourceId resourceId, YouTubelistSongThumbnails thumbnails);
        private record YouTubelistSongResourceId(string videoId);
        private record YouTubelistSongThumbnails(YouTubelistSongThumbnailsHigh high);
        private record YouTubelistSongThumbnailsHigh(string url);

    }
}