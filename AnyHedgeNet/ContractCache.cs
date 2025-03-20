using System.Collections.Concurrent;

namespace AnyHedgeNet;

internal abstract class Cache<T>
{
    public readonly ConcurrentDictionary<string, T> Dictionary;

    public Cache()
    {
        Dictionary = Load();
    }

    protected abstract string Filename { get; }   
    
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
        string cacheFile = Filename;
        string json = JsonConvert.SerializeObject(Dictionary, Formatting.Indented);
        File.WriteAllText(cacheFile, json);
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