namespace AnyHedgeNet;

public static class ContractAddressCache
{
    public static Dictionary<string, string?> Instance = Load();
    private const string _contractsCacheFilename = "contracts_cache.json";   
    
    private static Dictionary<string, string?> Load()
    {
        string cacheFile = _contractsCacheFilename;
        if (File.Exists(cacheFile))
        {
            string json = File.ReadAllText(cacheFile);
            return JsonConvert.DeserializeObject<Dictionary<string, string?>>(json) ?? new Dictionary<string, string?>();
        }
        return new Dictionary<string, string?>();
    }

    public static void Save()
    {
        string cacheFile = _contractsCacheFilename;
        string json = JsonConvert.SerializeObject(Instance, Formatting.Indented);
        File.WriteAllText(cacheFile, json);
    }
}