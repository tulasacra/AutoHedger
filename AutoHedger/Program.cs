using AnyHedgeNet;
using BitcoinCash;
using OraclesCash;
using Timer = System.Timers.Timer;

namespace AutoHedger
{
    class Program
    {
        private static Timer timer;
        const int Minutes = 15;
        
        private static readonly Spinner spinner = new Spinner();

        private const string delimiter = "--------------------------------------------------------------------------------";
        private static readonly string delimiterBold = $"{Environment.NewLine}===================================================================================================={Environment.NewLine}";
        private static AnyHedgeManager AnyHedge;

        static async Task Main(string[] args)
        {
            AnyHedge = new AnyHedgeManager(AppSettings.AccountKey);

            Console.Write("Reading OracleMetadata ..");
            CurrencyConfig[] accounts = await CurrencyConfig.Get(AppSettings.Wallets);
            
            timer = new Timer(TimeSpan.FromMinutes(Minutes));
            timer.Elapsed += async (sender, e) => await DisplayData(accounts);
            timer.AutoReset = true;
            timer.Enabled = true;
            
            await DisplayData(accounts);
            
            while (true)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true);
                    if (key.Key == ConsoleKey.Q)
                    {
                        break;
                    }
                }
                await Task.Delay(100);
            }
        }

        private static async Task DisplayData(CurrencyConfig[] accounts)
        {
            spinner.Stop();
            Console.Clear();
            Console.WriteLine($"Checking at: {DateTime.Now}");
            Console.WriteLine($"Minimum desired APY: {AppSettings.MinimumApy} %");

            try
            {
                Console.Write("Reading contracts ..");
                var contractAddresses = await AnyHedge.GetContractAddresses();
                var contracts = await AnyHedge.GetContracts(contractAddresses);
                Console.WriteLine("OK");
                
                Console.Write("Reading premiums ..");
                const string counterLeverage = "5"; //only check 20% hedge
                var premiumData = (await Premiums.GetPremiums(counterLeverage, 0))
                    .Where(x => x.Apy >= AppSettings.MinimumApy)
                    .ToList();
                Console.WriteLine("OK");

                foreach (var account in accounts)
                {
                    Console.WriteLine(delimiterBold);
                    await DisplayData(account, contracts, premiumData.Where(x=>x.CurrencyOracleKey == account.OracleKey).ToList());
                }
            }
            catch (Exception ex)
            {
                ConsoleWidgets.WriteLine($"Something went wrong (retrying in {Minutes} minutes): {ex.Message}", ConsoleColor.Red);
            }

            Console.WriteLine(delimiterBold);
            Console.Write("Press [Q] to exit the program.. ");
            //spinner.Start(); prevents scrolling :(
        }

        private static async Task DisplayData(CurrencyConfig account, List<Contract> contracts, List<PremiumDataItem> premiumData)
        {

            var oracleKey = account.OracleKey;
            OracleMetadata? oracleMetadata = account.OracleMetadata;
            decimal latestPrice = await OraclesCashService.GetLatestPrice(oracleKey, oracleMetadata);

            decimal? walletBalanceBch = null;
            decimal? walletBalance = null;
            try
            {
                if (account.Wallet.HasAddress)
                {
                    var bchClient = new BitcoinCashClient();
                    walletBalanceBch = (decimal)bchClient.GetWalletBalances(new List<string>() { account.Wallet.Address }).First().Value / 100_000_000;
                    walletBalance = walletBalanceBch.Value * latestPrice;
                }
            }
            catch (Exception ex)
            {
                ConsoleWidgets.WriteLine($"Error getting wallet balance: {ex.Message}", ConsoleColor.Red);
            }

            decimal? contractsBalanceBch = null;
            decimal? contractsBalance = null;
            decimal? bchAcquisitionCostFifo = null;
            try
            {
                var activeContracts = contracts
                    .Where(x => x.Parameters.OraclePublicKey == oracleKey)
                    .Where(x => x.Fundings[0].Settlement == null).ToList();
                var settledContracts = contracts
                    .Where(x => x.Parameters.OraclePublicKey == oracleKey)
                    .Where(x => x.Fundings[0].Settlement != null).ToList();
                contractsBalance = activeContracts.Sum(c => c.Metadata.NominalUnits) / oracleMetadata.ATTESTATION_SCALING;
                contractsBalanceBch = contractsBalance / latestPrice;
                bchAcquisitionCostFifo = CalculateFifoCost(walletBalanceBch, settledContracts, oracleMetadata);
            }
            catch (Exception ex)
            {
                ConsoleWidgets.WriteLine($"Error getting contract balance: {ex.Message}", ConsoleColor.Red);
            }

            Console.WriteLine($"BCH acquisition cost FIFO: {bchAcquisitionCostFifo.Format(8, 24)} {account.Wallet.Currency}");
            var priceDelta = (latestPrice - bchAcquisitionCostFifo) / bchAcquisitionCostFifo * 100;
            Console.WriteLine($"Latest price from OraclesCash: {latestPrice,20:N8} {account.Wallet.Currency} ({priceDelta.Format(2, 0, true)} %)");
            Console.WriteLine(delimiter);

            if (account.Wallet.HasAddress || !string.IsNullOrEmpty(AppSettings.AccountKey))
            {
                DisplayBalances(walletBalanceBch, walletBalance, contractsBalanceBch, contractsBalance, oracleMetadata, account.Wallet);
            }

            if (premiumData.Any())
            {
                //Console.WriteLine("Sorted by amount:");
                //DisplayPremiumsData(premiumData);
                //Console.WriteLine("Sorted by duration:");
                var premiumDataByDuration = premiumData.OrderBy(x => x.Duration).ToList();
                Console.WriteLine(delimiter);
                DisplayPremiumsData(premiumDataByDuration);
            }

            if (walletBalanceBch.HasValue && premiumData.Any())
            {
                var bestContractParameters = GetBestContractParameters(premiumData, walletBalanceBch.Value);
                if (bestContractParameters.HasValue)
                {
                    Console.WriteLine(delimiter);
                    Console.WriteLine($"Suggested contract parameters: {bestContractParameters.Value.amount} BCH, {bestContractParameters.Value.duration} days");
                    if (!string.IsNullOrEmpty(account.Wallet.PrivateKeyWIF))
                    {
                        await AnyHedge.CreateContract(account.Wallet.PrivateKeyWIF);
                    }
                }
            }
        }

        private static decimal? CalculateFifoCost(decimal? walletBalance, List<Contract> settledContracts, OracleMetadata oracleMetadata)
        {
            if (!walletBalance.HasValue || walletBalance == 0 || !settledContracts.Any()) return null;

            decimal totalCost = 0;
            decimal remainingBalance = walletBalance.Value;

            foreach (var contract in settledContracts.OrderByDescending(c => c.Parameters.MaturityTimestamp)) //todo actual settlement timestamp
            {
                if (remainingBalance == 0) break;

                var settlement = contract.Fundings[0].Settlement;

                decimal contractAmount = Math.Min(remainingBalance, settlement.ShortPayoutInSatoshis / 100_000_000m);
                decimal contractPrice = (decimal)settlement.SettlementPrice / oracleMetadata.ATTESTATION_SCALING;


                totalCost += contractAmount * contractPrice;
                remainingBalance -= contractAmount;
            }

            return totalCost / walletBalance;
        }

        private static void DisplayBalances(decimal? walletBalanceBch, decimal? walletBalance, decimal? contractsBalanceBch, decimal? contractsBalance,
            OracleMetadata oracleMetadata, WalletConfig wallet)
        {
            int assetDecimals = oracleMetadata.ATTESTATION_SCALING.ToString().Length - 1;

            var totalBch = walletBalanceBch + contractsBalanceBch;
            decimal? walletPercent = null;
            decimal? contractsPercent = null;
            if (totalBch.HasValue)
            {
                walletPercent = walletBalanceBch / totalBch * 100;
                contractsPercent = contractsBalanceBch / totalBch * 100;
            }

            List<List<string>> rows =
            [
                ["", "BCH", wallet.Currency.ToString(), "%"],
                ["Wallet balance:          ", walletBalanceBch.Format(), walletBalance.Format(assetDecimals), walletPercent.Format(2, 7)],
                ["Active contracts balance:", contractsBalanceBch.Format(), contractsBalance.Format(assetDecimals), contractsPercent.Format(2, 7)],
                ["Total balance:           ", totalBch.Format(), (walletBalance + contractsBalance).Format(assetDecimals), ""]
            ];

            ConsoleWidgets.DisplayTable(rows, borders: false);
        }

        private static void DisplayPremiumsData(List<PremiumDataItem> premiumData)
        {
            List<List<string>> rows = [["Amount (BCH)", "Duration (days)", "Premium (%)", "APY (%)"]];

            foreach (var item in premiumData)
            {
                rows.Add([
                    item.Amount.ToString(),
                    item.Duration.ToString(),
                    item.PremiumInfo.Total.ToString("F2"),
                    item.Apy.ToString("F2")
                ]);
            }

            ConsoleWidgets.DisplayTable(rows, borders: false);
        }

        private static (decimal amount, double duration)? GetBestContractParameters(List<PremiumDataItem> premiumData, decimal walletBalanceBch)
        {
            var candidates = premiumData
                .GroupBy(x => x.Amount)
                .ToList();

            var candidatesThatFitWholeBalance = candidates.Where(x => x.Key >= walletBalanceBch).ToList();
            if (candidatesThatFitWholeBalance.Any())
            {
                var bestDuration = candidatesThatFitWholeBalance.OrderBy(x => x.Key).First().OrderBy(x => x.Apy).Last().Duration;
                return (walletBalanceBch, bestDuration);
            }

            var bestCandidate = candidates.OrderBy(x => x.Key).Last().OrderBy(x => x.Apy).Last();
            return (bestCandidate.Amount, bestCandidate.Duration);
        }
    }
}