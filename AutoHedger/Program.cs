using AnyHedgeNet;
using BitcoinCash;
using OraclesCash;
using Timer = System.Timers.Timer;

namespace AutoHedger
{
    class Program
    {
        private const decimal bchAcquisitionCostFifo = 0.00571983m; //todo calculate

        private static readonly string currencyOracleKey = OracleKeys.Keys[AppSettings.Currency];

        private static Timer timer;
        const int Minutes = 15;

        static async Task Main(string[] args)
        {
            timer = new Timer(TimeSpan.FromMinutes(Minutes));
            timer.Elapsed += async (sender, e) => await DisplayData();
            timer.AutoReset = true;
            timer.Enabled = true;


            await DisplayData();
            Console.ReadLine();
        }

        private static async Task DisplayData()
        {
            const string delimiter = "--------------------------------------------------------------------------------";
            Console.Clear();
            Console.WriteLine($"Checking at: {DateTime.Now}");
            Console.WriteLine($"Minimum desired APY: {AppSettings.MinimumApy} %");
            Console.WriteLine(delimiter);

            try
            {
                const string counterLeverage = "5"; //only check 20% hedge
                
                OracleMetadata oracleMetadata = await OraclesCashService.GetMetadata(currencyOracleKey);
                decimal latestPrice = await OraclesCashService.GetLatestPrice(currencyOracleKey, oracleMetadata);


                Console.WriteLine($"BCH acquisition cost FIFO: {bchAcquisitionCostFifo,19:F8}");
                var priceDelta = (latestPrice - bchAcquisitionCostFifo) / bchAcquisitionCostFifo * 100;
                string status = priceDelta >= 0 ? "OK" : "";
                Console.WriteLine($"Latest price from OraclesCash: {latestPrice,15:F8} ({priceDelta:+0.00;-0.00;+0.00} %) {status}");
                Console.WriteLine(delimiter);


                decimal? walletBalanceBch = null;
                decimal? walletBalance = null;
                try
                {
                    var bchClient = new BitcoinCashClient();
                    walletBalanceBch = (decimal)bchClient.GetWalletBalances(new List<string>() { AppSettings.WalletAddress }).First().Value / 100_000_000;
                    walletBalance = walletBalanceBch.Value * latestPrice;
                    Console.WriteLine($"Wallet balance: {walletBalanceBch,26:F8} BCH {walletBalance,20:F8} {AppSettings.Currency}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error getting wallet balance: {ex.Message}");
                }

                decimal? contractsBalanceBch = null;
                decimal? contractsBalance = null;
                try
                {
                    var anyHedge = new AnyHedgeManager(AppSettings.AccountKey);
                    var contractAddresses = await anyHedge.GetContractAddresses();
                    var contracts = await anyHedge.GetContracts(contractAddresses);
                    var activeContracts = contracts.Where(x=> x.Fundings[0].Settlement == null).ToList();
                    contractsBalance = activeContracts.Sum(c => c.Metadata.NominalUnits) / oracleMetadata.ATTESTATION_SCALING;
                    contractsBalanceBch = contractsBalance / latestPrice;
                    Console.WriteLine($"Active contracts balance: {contractsBalanceBch,16:F8} BCH {contractsBalance,16} BTC");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error getting contract balance: {ex.Message}");
                }

                if (walletBalanceBch.HasValue && contractsBalanceBch.HasValue)
                {
                    Console.WriteLine($"Total balance: {walletBalanceBch + contractsBalanceBch,27:F8} BCH {walletBalance + contractsBalance,20:F8} {AppSettings.Currency}");
                }


                Console.WriteLine(delimiter);
                var premiumData = (await Premiums.GetPremiums(currencyOracleKey, counterLeverage, 0))
                    .Where(x => x.Apy >= AppSettings.MinimumApy)
                    .ToList();
                //Console.WriteLine("Sorted by amount:");
                //DisplayPremiumsData(premiumData);
                //Console.WriteLine("Sorted by duration:");
                var premiumDataByDuration = premiumData.OrderBy(x => x.Duration).ToList();
                DisplayPremiumsData(premiumDataByDuration);

                if (walletBalanceBch.HasValue && premiumData.Any())
                {
                    var bestContractParameters = GetBestContractParameters(premiumData, walletBalanceBch.Value);
                    if (bestContractParameters.HasValue)
                    {
                        Console.WriteLine(delimiter);
                        Console.WriteLine($"Suggested contract parameters: {bestContractParameters.Value.amount} BCH, {bestContractParameters.Value.duration} days");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Something went wrong (retry in {Minutes} minutes): {ex.Message}");
            }

            Console.WriteLine(delimiter);
            Console.WriteLine("Press [Enter] to exit the program.");
        }

        private static void DisplayPremiumsData(List<PremiumDataItem> premiumData)
        {
            Console.WriteLine("| Amount (BCH) | Duration (days) | Premium (%) | APY (%) |");
            Console.WriteLine("|--------------|-----------------|-------------|---------|");

            foreach (var item in premiumData)
            {
                Console.WriteLine($"| {item.Amount,12} | {item.Duration,15} | {item.PremiumInfo.Total,11:F2} | {item.Apy,7:F2} |");
            }
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