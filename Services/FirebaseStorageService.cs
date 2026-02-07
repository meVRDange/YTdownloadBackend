using Google.Cloud.Storage.V1;
using Google.Apis.Auth.OAuth2;
using System.Security.Cryptography;

namespace YTdownloadBackend.Services
{
    public interface IFirebaseStorageService
    {
        Task<bool> FileExistsAsync(string storagePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Uploads a local file to Firebase Storage and returns the storage path.
        /// </summary>
        Task<string?> UploadFileAsync(string localFilePath, string storagePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Generates a signed download URL with optional expiration (default 48 hours).
        /// </summary>
        Task<string?> GetDownloadUrlAsync(string storagePath, TimeSpan? duration = null);

        /// <summary>
        /// Deletes a file from Firebase Storage.
        /// </summary>
        Task<bool> DeleteFileAsync(string storagePath, CancellationToken cancellationToken = default);


    }

    public class FirebaseStorageService : IFirebaseStorageService
    {
        private StorageClient? _storageClient;
        private readonly object _clientLock = new();
        private readonly string _bucketName;
        private readonly ILogger<FirebaseStorageService> _logger;
        private ServiceAccountCredential? _serviceAccountCredential;

        public FirebaseStorageService(string bucketName, ILogger<FirebaseStorageService> logger)
        {
            _bucketName = bucketName ?? throw new ArgumentNullException(nameof(bucketName));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Checks if a file exists in Firebase Storage.
        /// </summary>
        public async Task<bool> FileExistsAsync(string storagePath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(storagePath))
            {
                _logger.LogError("Storage path is null or empty");
                return false;
            }

            try
            {
                var storageClient = GetStorageClient();
                var @object = await storageClient.GetObjectAsync(_bucketName, storagePath, null, cancellationToken);
                return @object != null;
            }
            catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogInformation("File not found in Firebase Storage: gs://{BucketName}/{StoragePath}",
                    _bucketName, storagePath);
                return false;
            }
            catch (Google.GoogleApiException ex)
            {
                _logger.LogError(ex, "Error checking file existence in Firebase Storage: {Message} (StatusCode: {StatusCode})",
                    ex.Message, ex.HttpStatusCode);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error checking file existence in Firebase Storage: {Message}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Lazily initializes the StorageClient on first use.
        /// </summary>
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
                    _logger.LogInformation("Google Cloud Storage client initialized successfully.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to initialize Google Cloud Storage client. Ensure Application Default Credentials are configured.");
                    throw;
                }
            }

            return _storageClient;
        }

        /// <summary>
        /// Lazily loads the service account credential for signing URLs.
        /// </summary>
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
                    {
                        throw new InvalidOperationException("GOOGLE_APPLICATION_CREDENTIALS environment variable not set.");
                    }

                    using (var stream = new FileStream(credentialsPath, FileMode.Open, FileAccess.Read))
                    {
                        _serviceAccountCredential = ServiceAccountCredential.FromServiceAccountData(stream);
                    }

                    _logger.LogInformation("Service account credential loaded for signing URLs.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load service account credential for URL signing.");
                    throw;
                }
            }

            return _serviceAccountCredential;
        }

        public async Task<string?> UploadFileAsync(string localFilePath, string storagePath, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(localFilePath))
            {
                _logger.LogError("Local file not found: {FilePath}", localFilePath);
                return null;
            }

            try
            {
                var storageClient = GetStorageClient();
                var fileInfo = new FileInfo(localFilePath);
                _logger.LogInformation("Starting Firebase Storage upload: {FileName} ({FileSize} bytes) -> gs://{BucketName}/{StoragePath}",
                    fileInfo.Name, fileInfo.Length, _bucketName, storagePath);

                using (var fileStream = File.OpenRead(localFilePath))
                {
                    

                    var result = await storageClient.UploadObjectAsync(
                        _bucketName,
                        storagePath,
                        "audio/mpeg",
                        fileStream
                    );

                    _logger.LogInformation("Firebase Storage upload completed successfully: gs://{BucketName}/{StoragePath}",
                        _bucketName, storagePath);

                    return storagePath;
                }
            }
            catch (Google.GoogleApiException ex)
            {
                _logger.LogError(ex, "Google Cloud Storage exception during upload: {Message} (StatusCode: {StatusCode})",
                    ex.Message, ex.HttpStatusCode);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected exception during Firebase Storage upload: {Message}", ex.Message);
                return null;
            }
        }

        public async Task<string?> GetDownloadUrlAsync(string storagePath, TimeSpan? duration = null)
        {
            try
            {
                var storageClient = GetStorageClient();
                duration ??= TimeSpan.FromHours(48);

                _logger.LogInformation("Generating signed Firebase Storage download URL for: gs://{BucketName}/{StoragePath} (expires in {Duration})",
                    _bucketName, storagePath, duration);

                // Verify object exists
                var storageObject = await storageClient.GetObjectAsync(_bucketName, storagePath);
                if (storageObject == null)
                {
                    _logger.LogWarning("Storage object not found: gs://{BucketName}/{StoragePath}", _bucketName, storagePath);
                    return null;
                }

                // Generate signed URL using service account credentials
                var credential = GetServiceAccountCredential();
                var urlSigner = UrlSigner.FromCredential(credential);
                
                var signedUrl = urlSigner.Sign(
                    _bucketName,
                    storagePath,
                    duration.Value,
                    HttpMethod.Get
                );

                _logger.LogInformation("Signed download URL generated successfully, expires in {Duration}", duration);
                return signedUrl;
            }
            catch (Google.GoogleApiException ex)
            {
                _logger.LogError(ex, "Google Cloud Storage exception while generating download URL for {StoragePath}: {Message}",
                    storagePath, ex.Message);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate signed download URL for {StoragePath}: {Message}", storagePath, ex.Message);
                return null;
            }
        }

        public async Task<bool> DeleteFileAsync(string storagePath, CancellationToken cancellationToken = default)
        {
            try
            {
                var storageClient = GetStorageClient();
                await storageClient.DeleteObjectAsync(_bucketName, storagePath, null, cancellationToken);
                _logger.LogInformation("Firebase Storage object deleted: gs://{BucketName}/{StoragePath}", _bucketName, storagePath);
                return true;
            }
            catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Storage object not found (already deleted?): gs://{BucketName}/{StoragePath}", _bucketName, storagePath);
                return true;
            }
            catch (Google.GoogleApiException ex)
            {
                _logger.LogError(ex, "Google Cloud Storage exception during delete: {Message} (StatusCode: {StatusCode})",
                    ex.Message, ex.HttpStatusCode);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected exception during Firebase Storage delete: {Message}", ex.Message);
                return false;
            }
        }
    }
}