using AnyHedgeNet;
using OraclesCash;

namespace AutoHedger;

public class TermedDepositAccount
{
    public WalletConfig Wallet;
    public string OracleKey;
    public OracleMetadata OracleMetadata;

    public decimal LatestPrice;

    private TermedDepositAccount(WalletConfig wallet, OracleMetadata oracleMetadata, string? oracleKey = null)
    {
        Wallet = wallet;
        OracleMetadata = oracleMetadata;
        OracleKey = oracleKey ?? OracleKeys.Keys[wallet.Currency];
    }

    public static async Task<TermedDepositAccount[]> Get(List<WalletConfig> wallets)
    {
        List<Task<TermedDepositAccount>> tasks = new (wallets.Count);
        foreach (var wallet in wallets)
        {
            var oracleKey = OracleKeys.Keys[wallet.Currency];
            tasks.Add(Task.Run(async () =>
            {
                var metadata = await OraclesCashService.GetMetadata(oracleKey);
                return new TermedDepositAccount(wallet, metadata, oracleKey);
            }));
        }
        
        return await Task.WhenAll(tasks);
    }

    public static async Task UpdateLatestPrice(TermedDepositAccount[] accounts)
    {
        decimal[] latestPrice = await OraclesCashService.GetLatestPrice(accounts.Select(x=> (x.OracleKey, x.OracleMetadata)).ToArray());
        for (int i = 0; i < accounts.Length; i++)
        {
            accounts[i].LatestPrice = latestPrice[i];
        }
    }
}