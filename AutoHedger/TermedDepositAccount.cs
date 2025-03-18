using AnyHedgeNet;
using BitcoinCash;
using OraclesCash;

namespace AutoHedger;

public class TermedDepositAccount
{
    private const decimal _satsPerBch = 100_000_000m;

    public WalletConfig Wallet;
    public string OracleKey;
    public OracleMetadata OracleMetadata;

    public decimal LatestPrice;
    public decimal? WalletBalance;
    public decimal? WalletBalanceBch;
    
    public decimal? ContractsBalanceBch = null;
    public decimal? ContractsBalance = null;
    public decimal? BchAcquisitionCostFifo = null;

    private TermedDepositAccount(WalletConfig wallet, OracleMetadata oracleMetadata, string? oracleKey = null)
    {
        Wallet = wallet;
        OracleMetadata = oracleMetadata;
        OracleKey = oracleKey ?? OracleKeys.Keys[wallet.Currency];
    }

    public static async Task<TermedDepositAccount[]> Get(List<WalletConfig> wallets)
    {
        List<Task<TermedDepositAccount>> tasks = new(wallets.Count);
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
    
    public void UpdateContractInfo(List<Contract> contracts)
    {
        var oracleKey = this.OracleKey;
        OracleMetadata? oracleMetadata = this.OracleMetadata;
        
        var activeContracts = contracts
            .Where(x => x.Parameters.OraclePublicKey == oracleKey)
            .Where(x => !x.IsSettled).ToList();
        var settledContracts = contracts
            .Where(x => x.Parameters.OraclePublicKey == oracleKey)
            .Where(x => x.IsSettled).ToList();
        this.ContractsBalance = activeContracts.Sum(c => c.Metadata.NominalUnits) / oracleMetadata.ATTESTATION_SCALING;
        this.ContractsBalanceBch = this.ContractsBalance / this.LatestPrice;
        this.BchAcquisitionCostFifo = CalculateFifoCost(this.WalletBalanceBch, settledContracts, oracleMetadata);  
    }
    
    private static decimal? CalculateFifoCost(decimal? walletBalance, List<Contract> settledContracts, OracleMetadata oracleMetadata)
    {
        if (!walletBalance.HasValue || walletBalance == 0 || !settledContracts.Any()) return null;

        decimal totalCost = 0;
        decimal remainingBalance = walletBalance.Value;

        foreach (var contract in settledContracts.OrderByDescending(c => OraclesCashService.ParsePriceMessage(c.Fundings[0].Settlement.SettlementMessage, oracleMetadata.ATTESTATION_SCALING).messageSequence))
        {
            if (remainingBalance == 0) break;

            var settlement = contract.Fundings[0].Settlement;

            decimal contractAmount = Math.Min(remainingBalance, settlement.ShortPayoutInSatoshis / _satsPerBch);
            decimal contractPrice = (decimal)settlement.SettlementPrice / oracleMetadata.ATTESTATION_SCALING;


            totalCost += contractAmount * contractPrice;
            remainingBalance -= contractAmount;
        }

        return totalCost / (walletBalance - remainingBalance);
    }

    public static async Task UpdateLatestPrices(TermedDepositAccount[] accounts)
    {
        decimal[] latestPrice = await OraclesCashService.GetLatestPrice(accounts.Select(x => (x.OracleKey, x.OracleMetadata)).ToArray());
        for (int i = 0; i < accounts.Length; i++)
        {
            accounts[i].LatestPrice = latestPrice[i];
            accounts[i].WalletBalance = accounts[i].WalletBalanceBch * accounts[i].LatestPrice;
        }
    }

    public static async Task UpdateWalletBalances(TermedDepositAccount[] accounts)
    {
        var addresses = accounts
            .Where(x => x.Wallet.HasAddress)
            .Select(x => x.Wallet.Address)
            .ToList();

        var balanceBch = await ElectrumNetworkProvider.GetBalanceBCH(addresses);

        foreach (var account in accounts.Where(x => x.Wallet.HasAddress))
        {
            account.WalletBalanceBch = balanceBch[account.Wallet.Address];
            account.WalletBalance = account.WalletBalanceBch.Value * account.LatestPrice;
        }
    }
}