using System.Collections.Concurrent;

namespace AnyHedgeNet;

internal abstract class Cache<T>
{
    private readonly ConcurrentDictionary<string, T> Dictionary;
    private bool _hasUnsavedValues;

    public Cache()
    {
        Dictionary = Load();
        _hasUnsavedValues = false;
    }

    protected abstract string Filename { get; }

    #region Dictionary wrappers

    public bool ContainsKey(string key)
    {
        return Dictionary.ContainsKey(key);
    }

    public T Add(string key, T value)
    {
        _hasUnsavedValues = true;
        return Dictionary.GetOrAdd(key, value);
    }

    public T this[string key]
    {
        get => Dictionary[key];
        set
        {
            Dictionary[key] = value;
            _hasUnsavedValues = true;
        }
    }

    public IEnumerable<T> Values => Dictionary.Values;

    public bool TryGetValue(string key, out T value)
    {
        return Dictionary.TryGetValue(key, out value);
    }
    
    #endregion

    private ConcurrentDictionary<string, T> Load()
    {
        string cacheFile = Filename;
        if (File.Exists(cacheFile))
        {
            string json = File.ReadAllText(cacheFile);
            return JsonConvert.DeserializeObject<ConcurrentDictionary<string, T>>(json) ?? new ConcurrentDictionary<string, T>();
        }

        return new ConcurrentDictionary<string, T>();
    }

    public void Save()
    {
        if (_hasUnsavedValues)
        {
            string json = JsonConvert.SerializeObject(Dictionary, Formatting.Indented);
            File.WriteAllText(Filename, json);
            _hasUnsavedValues = false;
        }
    }
}

internal class TxMetadataCache : Cache<AnyHedgeManager.TxMetadata>
{
    protected override string Filename => "tx_metadata_cache.json";

    public static TxMetadataCache Instance = new();
}

internal class TxCache : Cache<BlockchairApi.Transaction>
{
    protected override string Filename => "tx_cache.json";

    public static TxCache Instance = new();
}

internal class ContractCache : Cache<Contract>
{
    protected override string Filename => "contract_cache.json";

    public static ContractCache Instance = new();
}