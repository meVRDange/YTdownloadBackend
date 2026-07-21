using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace YTdownloadBackend.Services.Storage;

/// <summary>
/// Local-disk implementation of <see cref="IStorageProvider"/>.
/// Files are stored in the app's "downloads" folder and served via static file
/// middleware (protected by JWT ownership checks).
///
/// Configuration (appsettings.json):
/// <code>
/// "Storage": {
///   "Provider": "Local",
///   "Local": {
///     "BaseUrl": "https://api.vdange.site",
///     "DownloadsRoot": "downloads"
///   }
/// }
/// </code>
/// </summary>
public class LocalStorageProvider : IStorageProvider
{
    public string Name => "Local";

    private readonly string _downloadsRoot;
    private readonly string _baseUrl;
    private readonly ILogger<LocalStorageProvider> _logger;

    public LocalStorageProvider(
        IOptions<StorageProviderOptions> options,
        ILogger<LocalStorageProvider> logger)
    {
        var local = options?.Value?.Local
            ?? throw new InvalidOperationException("Storage:Local configuration is missing.");

        _downloadsRoot = string.IsNullOrWhiteSpace(local.DownloadsRoot)
            ? Path.Combine(Directory.GetCurrentDirectory(), "downloads")
            : Path.GetFullPath(local.DownloadsRoot);

        _baseUrl = (local.BaseUrl ?? string.Empty).TrimEnd('/');
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        Directory.CreateDirectory(_downloadsRoot);

        _logger.LogInformation("Local storage initialized: root={Root}, baseUrl={BaseUrl}",
            _downloadsRoot, string.IsNullOrEmpty(_baseUrl) ? "(not set)" : _baseUrl);
    }

    public Task<bool> FileExistsAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(storagePath))
        {
            _logger.LogError("Storage path is null or empty");
            return Task.FromResult(false);
        }

        var fullPath = GetFullPath(storagePath);
        var exists = File.Exists(fullPath);
        return Task.FromResult(exists);
    }

    public async Task<string?> UploadFileAsync(
        string localFilePath,
        string storagePath,
        string contentType = "audio/mpeg",
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(localFilePath))
        {
            _logger.LogError("Local file not found: {FilePath}", localFilePath);
            return null;
        }

        try
        {
            var destPath = GetFullPath(storagePath);

            // Create directory if needed
            var dir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // If the source is already at the destination, no copy needed
            if (string.Equals(
                Path.GetFullPath(localFilePath),
                Path.GetFullPath(destPath),
                StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("File already in place: {Path}", destPath);
                return storagePath;
            }

            // Copy the file to the downloads directory
            await using (var sourceStream = new FileStream(localFilePath, FileMode.Open, FileAccess.Read,
                FileShare.Read, 8192, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var destStream = new FileStream(destPath, FileMode.Create, FileAccess.Write,
                FileShare.None, 8192, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await sourceStream.CopyToAsync(destStream, cancellationToken);
            }

            var fileInfo = new FileInfo(destPath);
            _logger.LogInformation("Local copy completed: {Source} -> {Dest} ({Size} bytes)",
                Path.GetFileName(localFilePath), destPath, fileInfo.Length);

            return storagePath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Local upload/copy failed: {Message}", ex.Message);
            return null;
        }
    }

    public Task<string?> GetDownloadUrlAsync(string storagePath, TimeSpan? duration = null)
    {
        if (string.IsNullOrEmpty(storagePath))
        {
            _logger.LogError("Storage path is null or empty");
            return Task.FromResult<string?>(null);
        }

        // Normalize path separators for URLs
        var urlPath = storagePath.Replace('\\', '/');

        // Return a URL that goes through the static files middleware.
        // The owning user is enforced by middleware in Program.cs.
        var url = string.IsNullOrEmpty(_baseUrl)
            ? $"/downloads/{urlPath}"
            : $"{_baseUrl}/downloads/{urlPath}";

        _logger.LogInformation("Generated local download URL: {Url}", url);
        return Task.FromResult<string?>(url);
    }

    public Task<bool> DeleteFileAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(storagePath))
        {
            _logger.LogError("Storage path is null or empty");
            return Task.FromResult(false);
        }

        try
        {
            var fullPath = GetFullPath(storagePath);
            if (!File.Exists(fullPath))
            {
                _logger.LogWarning("File not found for deletion: {Path}", fullPath);
                return Task.FromResult(false);
            }

            File.Delete(fullPath);
            _logger.LogInformation("Deleted local file: {Path}", fullPath);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete local file: {Message}", ex.Message);
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Resolves a storage-relative path to the absolute filesystem path,
    /// preventing path traversal outside the downloads root.
    /// </summary>
    private string GetFullPath(string storagePath)
    {
        // Normalize separators to the OS default
        var normalized = storagePath.Replace('/', Path.DirectorySeparatorChar)
                                    .Replace('\\', Path.DirectorySeparatorChar);

        var fullPath = Path.GetFullPath(Path.Combine(_downloadsRoot, normalized));

        // Guard against path traversal
        if (!fullPath.StartsWith(_downloadsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(fullPath, _downloadsRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException(
                $"Path traversal detected: '{storagePath}' resolves outside downloads root.");
        }

        return fullPath;
    }
}
