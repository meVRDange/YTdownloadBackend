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

    /// <summary>
    /// AWS S3 specific settings. Populated from the "Storage:S3" section.
    /// Unused until an S3 provider is implemented, but kept here so config
    /// can be authored ahead of time.
    /// </summary>
    public S3Options S3 { get; set; } = new();

    /// <summary>Settings for the Firebase / GCS provider.</summary>
    public sealed class FirebaseOptions
    {
        /// <summary>
        /// The GCS bucket name (e.g. "ytdownloder"). Maps to the legacy
        /// "Firebase:StorageBucket" value.
        /// </summary>
        public string Bucket { get; set; } = string.Empty;
    }

    /// <summary>Settings for a future AWS S3 provider.</summary>
    public sealed class S3Options
    {
        public string Bucket { get; set; } = string.Empty;
        public string Region { get; set; } = "us-east-1";
    }
}
