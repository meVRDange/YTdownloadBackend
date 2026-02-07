using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
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
        private const int ApiMaxPerPage = 50;
        private const int MaxTotalItems = 2000;

        public YouTubeService(HttpClient httpClient, IConfiguration config)
        {
            _http = httpClient;
            // Use environment variable first, fallback to config (for dev)
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
            var results = new List<PlaylistSong>();
            string? pageToken = null;

            try
            {
                do
                {
                    var tokenPart = string.IsNullOrEmpty(pageToken) ? "" : $"&pageToken={pageToken}";
                    var url = $"https://www.googleapis.com/youtube/v3/playlistItems" +
                              $"?part=snippet&playlistId={playlistId}&key={_apiKey}&maxResults={ApiMaxPerPage}{tokenPart}";

                    var resp = await _http.GetFromJsonAsync<YouTubelistSongPlaylistResponse>(url);
                    if (resp?.items == null || resp.items.Count == 0)
                        break;

                    foreach (var item in resp.items)
                    {
                        var thumb = item.snippet?.thumbnails?.high?.url
                                    ?? item.snippet?.thumbnails?.medium?.url
                                    ?? item.snippet?.thumbnails?.@default?.url;

                        results.Add(new PlaylistSong
                        {
                            PlaylistId = playlistId,
                            VideoId = item.snippet?.resourceId?.videoId ?? string.Empty,
                            Title = item.snippet?.title,
                            ThumbnailUrl = thumb
                        });

                        if (results.Count >= MaxTotalItems)
                            break;
                    }

                    if (results.Count >= MaxTotalItems)
                        break;

                    pageToken = resp.nextPageToken;

                } while (!string.IsNullOrEmpty(pageToken));

                // Ensure we never return more than the configured maximum
                return results.Take(MaxTotalItems).ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"YouTube API error: {ex.Message}");
                return null;
            }
        }

        // DTOs for JSON mapping
        private record YouTubeTitlePlaylistResponse(List<YouTubeTitlePlaylistItem>? items);
        private record YouTubeTitlePlaylistItem(YouTubeTitleSnippet? snippet);
        private record YouTubeTitleSnippet(string? title);

        private record YouTubelistSongPlaylistResponse(List<YouTubelistSongPlaylistItem>? items, string? nextPageToken);
        private record YouTubelistSongPlaylistItem(YouTubelistSongSnippet? snippet);
        private record YouTubelistSongSnippet(string? title, YouTubelistSongResourceId? resourceId, YouTubelistSongThumbnails? thumbnails);
        private record YouTubelistSongResourceId(string? videoId);

        private record YouTubelistSongThumbnails(
            YouTubelistSongThumbnailsHigh? high,
            YouTubelistSongThumbnailsMedium? medium,
            [property: JsonPropertyName("default")] YouTubelistSongThumbnailsDefault? @default
        );

        private record YouTubelistSongThumbnailsHigh(string? url);
        private record YouTubelistSongThumbnailsMedium(string? url);
        private record YouTubelistSongThumbnailsDefault(string? url);
    }
}