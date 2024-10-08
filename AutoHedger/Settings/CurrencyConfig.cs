using AnyHedgeNet;
using OraclesCash;

namespace AutoHedger;

public class CurrencyConfig
{
    public WalletConfig Wallet;
    public string OracleKey;
    public OracleMetadata OracleMetadata;

    private CurrencyConfig(WalletConfig wallet, OracleMetadata oracleMetadata, string? oracleKey = null)
    {
        Wallet = wallet;
        OracleMetadata = oracleMetadata;
        OracleKey = oracleKey ?? OracleKeys.Keys[wallet.Currency];
    }

    public static async Task<CurrencyConfig[]> Get(List<WalletConfig> wallets)
    {
        List<Task<CurrencyConfig>> tasks = new (wallets.Count);
        foreach (var wallet in wallets)
        {
            var oracleKey = OracleKeys.Keys[wallet.Currency];
            tasks.Add(Task.Run(async () =>
            {
                var metadata = await OraclesCashService.GetMetadata(oracleKey);
                return new CurrencyConfig(wallet, metadata, oracleKey);
            }));
        }
        
        return await Task.WhenAll(tasks);
    }
}