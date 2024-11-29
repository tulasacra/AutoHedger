using AnyHedgeNet;
using BitcoinCash;
using OraclesCash;

namespace AutoHedger;

public class TermedDepositAccount
{
    public WalletConfig Wallet;
    public string OracleKey;
    public OracleMetadata OracleMetadata;

    public decimal LatestPrice;
    public decimal? WalletBalance;
    public decimal? WalletBalanceBch;

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

    public static async Task UpdateLatestPrices(TermedDepositAccount[] accounts)
    {
        decimal[] latestPrice = await OraclesCashService.GetLatestPrice(accounts.Select(x=> (x.OracleKey, x.OracleMetadata)).ToArray());
        for (int i = 0; i < accounts.Length; i++)
        {
            accounts[i].LatestPrice = latestPrice[i];
            accounts[i].WalletBalance = accounts[i].WalletBalanceBch * accounts[i].LatestPrice;
        }
    }
    
    public static void UpdateWalletBalances(TermedDepositAccount[] accounts)
    {
        var bchClient = new BitcoinCashClient();
        var results = bchClient.GetWalletBalances(accounts.Select(x => x.Wallet.Address).ToList());

        foreach (var account in accounts.Where(x=>x.Wallet.HasAddress))
        {
            account.WalletBalanceBch = results.FirstOrDefault(x => x.Key == account.Wallet.Address).Value / 100_000_000m;
            account.WalletBalance = account.WalletBalanceBch.Value * account.LatestPrice;
        }
    }
}