namespace YTdownloadBackend.Services.Storage;

/// <summary>
/// Resolves the active <see cref="IStorageProvider"/> at runtime based on
/// <see cref="StorageProviderOptions.Provider"/>. This is the seam that lets
/// consumers depend on a single abstraction while the concrete provider is
/// selected via configuration.
/// </summary>
public interface IStorageProviderFactory
{
    /// <summary>
    /// Returns the provider whose <see cref="IStorageProvider.Name"/> matches
    /// <see cref="StorageProviderOptions.Provider"/>. Throws if no match is found.
    /// </summary>
    IStorageProvider GetActiveProvider();

    /// <summary>
    /// Returns the provider registered under <paramref name="name"/>, or null
    /// if no such provider is registered. Useful for multi-provider scenarios.
    /// </summary>
    IStorageProvider? GetProvider(string name);
}
