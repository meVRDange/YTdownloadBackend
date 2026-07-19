namespace YTdownloadBackend.Services.Storage;

/// <summary>
/// Provider-neutral abstraction for object storage (e.g. Google Cloud Storage, S3, Azure Blob).
/// Consumers resolve the active provider via <see cref="IStorageProviderFactory"/> and should not
/// reference any provider-specific type directly.
/// </summary>
public interface IStorageProvider
{
    /// <summary>
    /// The provider name used for registration and resolution by the factory (e.g. "Firebase", "S3").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Checks whether an object exists at the given storage path.
    /// </summary>
    Task<bool> FileExistsAsync(string storagePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads a local file to storage and returns the storage path on success, or null on failure.
    /// </summary>
    /// <param name="localFilePath">Absolute path to the local file to upload.</param>
    /// <param name="storagePath">Provider-relative object key (e.g. "users/{userId}/songs/{fileName}").</param>
    /// <param name="contentType">MIME content type to assign to the uploaded object. Defaults to "audio/mpeg".</param>
    Task<string?> UploadFileAsync(
        string localFilePath,
        string storagePath,
        string contentType = "audio/mpeg",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a signed download URL for the object at the given storage path.
    /// </summary>
    /// <param name="storagePath">Provider-relative object key.</param>
    /// <param name="duration">Optional validity duration for the signed URL. Providers apply their own default when null.</param>
    Task<string?> GetDownloadUrlAsync(string storagePath, TimeSpan? duration = null);

    /// <summary>
    /// Deletes the object at the given storage path. Returns true if deleted, false if not found.
    /// </summary>
    Task<bool> DeleteFileAsync(string storagePath, CancellationToken cancellationToken = default);
}
