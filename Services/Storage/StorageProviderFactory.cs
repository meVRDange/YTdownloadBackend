using Microsoft.Extensions.Options;

namespace YTdownloadBackend.Services.Storage;

/// <summary>
/// Default implementation of <see cref="IStorageProviderFactory"/>. Holds a
/// collection of registered providers (keyed by <see cref="IStorageProvider.Name"/>)
/// and returns the one matching <see cref="StorageProviderOptions.Provider"/>.
/// </summary>
public sealed class StorageProviderFactory : IStorageProviderFactory
{
    private readonly IReadOnlyDictionary<string, IStorageProvider> _providers;
    private readonly StorageProviderOptions _options;

    public StorageProviderFactory(
        IEnumerable<IStorageProvider> providers,
        IOptions<StorageProviderOptions> options)
    {
        _options = options.Value;

        // Last registration wins if two providers share the same Name.
        _providers = providers
            .ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase);
    }

    public IStorageProvider GetActiveProvider()
    {
        var name = _options.Provider;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException(
                "Storage:Provider is not configured. Set 'Storage:Provider' in appsettings.json.");
        }

        if (_providers.TryGetValue(name, out var provider))
        {
            return provider;
        }

        var available = _providers.Count == 0
            ? "(none registered)"
            : string.Join(", ", _providers.Keys);

        throw new InvalidOperationException(
            $"No storage provider registered with name '{name}'. " +
            $"Available providers: {available}. " +
            $"Check the 'Storage:Provider' setting in appsettings.json.");
    }

    public IStorageProvider? GetProvider(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        _providers.TryGetValue(name, out var provider);
        return provider;
    }
}
