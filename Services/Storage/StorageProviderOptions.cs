namespace YTdownloadBackend.Services.Storage;

/// <summary>
/// Strongly-typed options bound to the "Storage" configuration section.
/// Controls which storage provider is active and supplies provider-specific settings.
///
/// Example appsettings.json:
/// <code>
/// "Storage": {
///   "Provider": "Firebase",
///   "Firebase": { "Bucket": "ytdownloder" },
///   "S3":       { "Bucket": "my-bucket", "Region": "us-east-1" }
/// }
/// </code>
/// </summary>
public sealed class StorageProviderOptions
{
    /// <summary>
    /// The name of the active provider. Must match the <see cref="IStorageProvider.Name"/>
    /// of a registered provider. Defaults to "Firebase" for backward compatibility.
    /// </summary>
    public string Provider { get; set; } = "Firebase";

    /// <summary>
    /// Firebase / Google Cloud Storage specific settings.
    /// Populated from the "Storage:Firebase" section.
    /// </summary>
    public FirebaseOptions Firebase { get; set; } = new();

    /// <summary>Settings for the Firebase / GCS provider.</summary>
    public sealed class FirebaseOptions
    {
        /// <summary>
        /// The GCS bucket name (e.g. "ytdownloder"). Maps to the legacy
        /// "Firebase:StorageBucket" value.
        /// </summary>
        public string Bucket { get; set; } = string.Empty;
    }

    /// <summary>Settings for the local-disk storage provider.</summary>
    public sealed class LocalOptions
    {
        /// <summary>Base URL for generating download links (e.g. "https://api.vdange.site").</summary>
        public string BaseUrl { get; set; } = string.Empty;
        /// <summary>Root folder for stored files. Defaults to "downloads" under the app root.</summary>
        public string DownloadsRoot { get; set; } = "downloads";
    }

    public LocalOptions Local { get; set; } = new();
}
