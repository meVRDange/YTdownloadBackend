using Google.Apis.Auth.OAuth2;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace YTdownloadBackend.Services.Storage
{
    /// <summary>
    /// Google Cloud Storage (Firebase Storage) implementation of <see cref="IStorageProvider"/>.
    /// Uses Application Default Credentials (GOOGLE_APPLICATION_CREDENTIALS env var) for both
    /// client creation and signed-URL generation.
    /// </summary>
    public class FirebaseStorageProvider : IStorageProvider
    {
        public string Name => "Firebase";

        private StorageClient? _storageClient;
        private ServiceAccountCredential? _serviceAccountCredential;
        private readonly object _clientLock = new();

        private readonly string _bucketName;
        private readonly ILogger<FirebaseStorageProvider> _logger;

        public FirebaseStorageProvider(IOptions<StorageProviderOptions> options, ILogger<FirebaseStorageProvider> logger)
        {
            var bucket = options?.Value?.Firebase?.Bucket
                ?? throw new InvalidOperationException("Storage:Firebase:Bucket is not configured.");
            _bucketName = bucket;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<bool> FileExistsAsync(string storagePath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(storagePath))
            {
                _logger.LogError("Storage path is null or empty");
                return false;
            }

            try
            {
                var client = GetStorageClient();
                var obj = await client.GetObjectAsync(_bucketName, storagePath, null, cancellationToken);
                return obj != null;
            }
            catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogInformation("File not found in storage: gs://{Bucket}/{Path}", _bucketName, storagePath);
                return false;
            }
            catch (Google.GoogleApiException ex)
            {
                _logger.LogError(ex, "Error checking file existence: {Message} (StatusCode: {StatusCode})",
                    ex.Message, ex.HttpStatusCode);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error checking file existence: {Message}", ex.Message);
                return false;
            }
        }

        public async Task<string?> UploadFileAsync(string localFilePath, string storagePath, string contentType = "audio/mpeg", CancellationToken cancellationToken = default)
        {
            if (!File.Exists(localFilePath))
            {
                _logger.LogError("Local file not found: {FilePath}", localFilePath);
                return null;
            }

            try
            {
                var client = GetStorageClient();
                var fileInfo = new FileInfo(localFilePath);
                _logger.LogInformation("Starting upload: {FileName} ({FileSize} bytes) -> gs://{Bucket}/{Path}",
                    fileInfo.Name, fileInfo.Length, _bucketName, storagePath);

                using (var fileStream = File.OpenRead(localFilePath))
                {
                    await client.UploadObjectAsync(_bucketName, storagePath, contentType, fileStream, cancellationToken: cancellationToken);
                    _logger.LogInformation("Upload completed: gs://{Bucket}/{Path}", _bucketName, storagePath);
                    return storagePath;
                }
            }
            catch (Google.GoogleApiException ex)
            {
                _logger.LogError(ex, "Storage exception during upload: {Message} (StatusCode: {StatusCode})",
                    ex.Message, ex.HttpStatusCode);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected exception during upload: {Message}", ex.Message);
                return null;
            }
        }

        public async Task<string?> GetDownloadUrlAsync(string storagePath, TimeSpan? duration = null)
        {
            try
            {
                var client = GetStorageClient();
                duration ??= TimeSpan.FromHours(48);

                _logger.LogInformation("Generating signed download URL for: gs://{Bucket}/{Path} (expires in {Duration})",
                    _bucketName, storagePath, duration);

                var storageObject = await client.GetObjectAsync(_bucketName, storagePath);
                if (storageObject == null)
                {
                    _logger.LogWarning("Storage object not found: gs://{Bucket}/{Path}", _bucketName, storagePath);
                    return null;
                }

                var credential = GetServiceAccountCredential();
                var urlSigner = UrlSigner.FromCredential(credential);
                var signedUrl = urlSigner.Sign(_bucketName, storagePath, duration.Value, HttpMethod.Get);

                _logger.LogInformation("Signed download URL generated, expires in {Duration}", duration);
                return signedUrl;
            }
            catch (Google.GoogleApiException ex)
            {
                _logger.LogError(ex, "Storage exception while generating download URL for {Path}: {Message}",
                    storagePath, ex.Message);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate signed download URL for {Path}: {Message}", storagePath, ex.Message);
                return null;
            }
        }

        public async Task<bool> DeleteFileAsync(string storagePath, CancellationToken cancellationToken = default)
        {
            try
            {
                var client = GetStorageClient();
                await client.DeleteObjectAsync(_bucketName, storagePath, null, cancellationToken);
                _logger.LogInformation("Storage object deleted: gs://{Bucket}/{Path}", _bucketName, storagePath);
                return true;
            }
            catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Storage object not found (already deleted?): gs://{Bucket}/{Path}", _bucketName, storagePath);
                return true;
            }
            catch (Google.GoogleApiException ex)
            {
                _logger.LogError(ex, "Storage exception during delete: {Message} (StatusCode: {StatusCode})",
                    ex.Message, ex.HttpStatusCode);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected exception during delete: {Message}", ex.Message);
                return false;
            }
        }

        private StorageClient GetStorageClient()
        {
            if (_storageClient != null)
                return _storageClient;

            lock (_clientLock)
            {
                if (_storageClient != null)
                    return _storageClient;

                try
                {
                    _logger.LogInformation("Initializing Google Cloud Storage client...");
                    _storageClient = StorageClient.Create();
                    _logger.LogInformation("Google Cloud Storage client initialized.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to initialize Google Cloud Storage client. Ensure Application Default Credentials are configured.");
                    throw;
                }
            }

            return _storageClient;
        }

        private ServiceAccountCredential GetServiceAccountCredential()
        {
            if (_serviceAccountCredential != null)
                return _serviceAccountCredential;

            lock (_clientLock)
            {
                if (_serviceAccountCredential != null)
                    return _serviceAccountCredential;

                try
                {
                    var credentialsPath = Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS");
                    if (string.IsNullOrEmpty(credentialsPath))
                        throw new InvalidOperationException("GOOGLE_APPLICATION_CREDENTIALS environment variable not set.");

                    using (var stream = new FileStream(credentialsPath, FileMode.Open, FileAccess.Read))
                    {
                        _serviceAccountCredential = ServiceAccountCredential.FromServiceAccountData(stream);
                    }

                    _logger.LogInformation("Service account credential loaded for URL signing.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load service account credential for URL signing.");
                    throw;
                }
            }

            return _serviceAccountCredential;
        }
    }
}
