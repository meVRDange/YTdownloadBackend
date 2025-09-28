using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

public interface IYouTubeService
{
    Task<string?> GetPlaylistTitleAsync(string playlistId);
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
            var resp = await _http.GetFromJsonAsync<YouTubePlaylistResponse>(url);
            return resp?.items?.FirstOrDefault()?.snippet?.title;
        }
        catch (Exception ex)
        {
            // Optionally log the error with ILogger
            Console.WriteLine($"YouTube API error: {ex.Message}");
            return null;
        }
    }

    // DTOs for JSON mapping
    private record YouTubePlaylistResponse(List<YouTubePlaylistItem> items);
    private record YouTubePlaylistItem(YouTubeSnippet snippet);
    private record YouTubeSnippet(string title);
}
