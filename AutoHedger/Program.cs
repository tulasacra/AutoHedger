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
            const string delimiter = "----------------------------------------------------------------------------------------------------";
            Console.Clear();
            Console.WriteLine($"Checking at: {DateTime.Now}");
            Console.WriteLine($"Minimum desired APY: {AppSettings.MinimumApy} %");
            Console.WriteLine(delimiter);

            try
            {
                const string counterLeverage = "5"; //only check 20% hedge
                
                OracleMetadata oracleMetadata = await OraclesCashService.GetMetadata(currencyOracleKey);
                decimal latestPrice = await OraclesCashService.GetLatestPrice(currencyOracleKey, oracleMetadata);


                Console.WriteLine($"BCH acquisition cost FIFO: {bchAcquisitionCostFifo,24:N8}");
                var priceDelta = (latestPrice - bchAcquisitionCostFifo) / bchAcquisitionCostFifo * 100;
                Console.WriteLine($"Latest price from OraclesCash: {latestPrice,20:N8} ({priceDelta:+0.00;-0.00;+0.00} %)");
                Console.WriteLine(delimiter);


                decimal? walletBalanceBch = null;
                decimal? walletBalance = null;
                try
                {
                    var bchClient = new BitcoinCashClient();
                    walletBalanceBch = (decimal)bchClient.GetWalletBalances(new List<string>() { AppSettings.WalletAddress }).First().Value / 100_000_000;
                    walletBalance = walletBalanceBch.Value * latestPrice;
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
                    var activeContracts = contracts
                        .Where(x=>x.Parameters.OraclePublicKey == currencyOracleKey)
                        .Where(x=> x.Fundings[0].Settlement == null).ToList();
                    contractsBalance = activeContracts.Sum(c => c.Metadata.NominalUnits) / oracleMetadata.ATTESTATION_SCALING;
                    contractsBalanceBch = contractsBalance / latestPrice;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error getting contract balance: {ex.Message}");
                }
                
                DisplayBalances(walletBalanceBch, walletBalance, contractsBalanceBch, contractsBalance, oracleMetadata);


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

        private static void DisplayBalances(decimal? walletBalanceBch, decimal? walletBalance, decimal? contractsBalanceBch, decimal? contractsBalance,
            OracleMetadata oracleMetadata)
        {
            string Format(decimal? value, int decimals = 8, int padSize = 17)
            {
                if (!value.HasValue)
                {
                    return "??";
                }

                return value.Value.ToString($"N{decimals}").PadLeft(padSize);
            }

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
                ["", "BCH", AppSettings.Currency.ToString(), "%"],
                ["Wallet balance:          ", Format(walletBalanceBch), Format(walletBalance, assetDecimals), Format(walletPercent, 2, 7)],
                ["Active contracts balance:", Format(contractsBalanceBch), Format(contractsBalance, assetDecimals), Format(contractsPercent, 2, 7)],
                ["Total balance:           ", Format(totalBch), Format(walletBalance + contractsBalance, assetDecimals), ""]
            ];

            DisplayTable(rows, borders: false);
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

            DisplayTable(rows, borders: false);
        }

        private static void DisplayTable(List<List<string>> rows, bool firstRowIsHeaders = true, bool borders = true)
        {
            if (rows == null || rows.Count == 0)
                return;

            int[] columnWidths = new int[rows[0].Count];
            for (int i = 0; i < rows[0].Count; i++)
            {
                columnWidths[i] = rows.Max(row => row[i].Length);
            }

            string separator = "|" + string.Join("|", columnWidths.Select(w => new string('-', w + 2))) + "|";

            if (borders)
                Console.WriteLine(separator);

            for (int i = 0; i < rows.Count; i++)
            {
                List<string> row = rows[i];
                string line = "|";

                for (int j = 0; j < row.Count; j++)
                {
                    line += $" {row[j].PadLeft(columnWidths[j])} |";
                }

                Console.WriteLine(line);

                if (i == 0 && firstRowIsHeaders)
                    Console.WriteLine(separator);
            }

            if (borders)
                Console.WriteLine(separator);
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