using System.Text;
using AnyHedgeNet;
using BitcoinCash;
using ConsoleWidgets;
using OraclesCash;
using Timer = System.Timers.Timer;

namespace AutoHedger
{
    class Program
    {
        private static Timer timer;
        const int Minutes = 15;

        private const string delimiter = "-------------------------------------------------------------------------------------";
        private static readonly string delimiterBold = $"{Environment.NewLine}===================================================================================================={Environment.NewLine}";
        private static AnyHedgeManager AnyHedge;
        private static Menu Menu = new();


        static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            AnyHedge = new AnyHedgeManager(AppSettings.AccountKey);

            Console.Write("Reading OracleMetadata ..");
            CurrencyConfig[] accounts = await CurrencyConfig.Get(AppSettings.Wallets);

            async void Refresh()
            {
                await DisplayData(accounts);
            }

            timer = new Timer(TimeSpan.FromMinutes(Minutes));
            timer.Elapsed += async (sender, e) => Refresh();
            timer.AutoReset = true;
            timer.Enabled = true;

            Menu.AddOption(ConsoleKey.R, "Refresh", Refresh);

            Refresh();
            await Menu.Start();
        }

        private static async Task DisplayData(CurrencyConfig[] accounts)
        {
            Console.Clear();
            Console.WriteLine($"Checking at: {DateTime.Now}");
            Console.WriteLine($"Minimum desired APY: {AppSettings.MinimumApy} %");

            var contractProposals = new List<string>();
            try
            {
                Console.Write("Reading contracts ..");
                var contractAddresses = await AnyHedge.GetContractAddresses();
                var contracts = await AnyHedge.GetContracts(contractAddresses);
                Console.WriteLine("OK");

                Console.Write("Reading premiums ..");
                const string counterLeverage = "5"; //only check 20% hedge
                var premiumData = (await Premiums.GetPremiums(counterLeverage, 0)).ToList();
                Console.WriteLine("OK");

                foreach (var account in accounts)
                {
                    Console.WriteLine(delimiterBold);
                    contractProposals.AddRange(await DisplayData(account, contracts, premiumData.Where(x => x.CurrencyOracleKey == account.OracleKey).ToList()));
                }
            }
            catch (Exception ex)
            {
                Widgets.WriteLine($"Something went wrong (retrying in {Minutes} minutes): {ex}", ConsoleColor.Red);
            }

            const string showContractProposal = "Show contract proposal";
            Menu.RemoveOptions(x => x.Value.Description.StartsWith(showContractProposal));
            for (int i = 0; i < contractProposals.Count; i++)
            {
                var proposal = contractProposals[i];
                Menu.AddOption((ConsoleKey)((int)ConsoleKey.D1 + i), $"{showContractProposal} {i+1}", () =>
                {
                    DisplayContractProposal(proposal);
                });
            }

            Console.WriteLine(delimiterBold);
            Menu.Show();
        }

        private static void DisplayContractProposal(string proposal)
        {
            Console.Clear();
            Console.WriteLine(proposal);
            Menu.Show();
        }

        private static async Task<List<string>> DisplayData(CurrencyConfig account, List<Contract> contracts, List<PremiumDataItem> premiumData)
        {
            var results = new List<string>();
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
                Widgets.WriteLine($"Error getting wallet balance: {ex.Message}", ConsoleColor.Red);
            }

            decimal? contractsBalanceBch = null;
            decimal? contractsBalance = null;
            decimal? bchAcquisitionCostFifo = null;

            var activeContracts = contracts
                .Where(x => x.Parameters.OraclePublicKey == oracleKey)
                .Where(x => x.Fundings[0].Settlement == null).ToList();
            var settledContracts = contracts
                .Where(x => x.Parameters.OraclePublicKey == oracleKey)
                .Where(x => x.Fundings[0].Settlement != null).ToList();
            contractsBalance = activeContracts.Sum(c => c.Metadata.NominalUnits) / oracleMetadata.ATTESTATION_SCALING;
            contractsBalanceBch = contractsBalance / latestPrice;
            bchAcquisitionCostFifo = CalculateFifoCost(walletBalanceBch, settledContracts, oracleMetadata);

            Console.WriteLine($"BCH acquisition cost FIFO: {bchAcquisitionCostFifo.Format(8, 24)} {account.Wallet.Currency}");
            var priceDelta = (latestPrice - bchAcquisitionCostFifo) / bchAcquisitionCostFifo * 100;
            Console.WriteLine($"Latest price from OraclesCash: {latestPrice,20:N8} {account.Wallet.Currency} (Δ {priceDelta.Format(2, 0, true)} %)");

            if (account.Wallet.HasAddress || !string.IsNullOrEmpty(AppSettings.AccountKey))
            {
                Console.WriteLine(delimiter);
                DisplayBalances(walletBalanceBch, walletBalance, contractsBalanceBch, contractsBalance, oracleMetadata, account.Wallet);
            }

            var premiumDataPlus = premiumData
                .Select(x => new PremiumDataItemPlus(x, priceDelta))
                .Where(x => x.Item.Apy >= AppSettings.MinimumApy ||
                            x.ApyPlusPriceDelta >= (decimal?)AppSettings.MinimumApy ||
                            x.YieldPlusPriceDeltaAnnualized >= (decimal?)AppSettings.MinimumApy)
                .ToList();

            if (premiumDataPlus.Any())
            {
                //Console.WriteLine("Sorted by amount:");
                //DisplayPremiumsData(premiumData);
                //Console.WriteLine("Sorted by duration:");
                var premiumDataByDuration = premiumDataPlus.OrderBy(x => x.Item.DurationDays).ToList();
                Console.WriteLine(delimiter);
                DisplayPremiumsData(premiumDataByDuration, priceDelta);
            }

            if (walletBalanceBch.HasValue && premiumDataPlus.Any() && !(priceDelta < 0))
            {
                var bestContractParameters = GetBestContractParameters_MaxApy(premiumDataPlus, walletBalanceBch.Value);
                if (bestContractParameters.HasValue)
                {
                    Console.WriteLine(delimiter);
                    var suggestedParameters = $"Suggested contract parameters: {bestContractParameters.Value.amount} BCH, {bestContractParameters.Value.premiumDataItem.Item.DurationDays} days";
                    Console.WriteLine(suggestedParameters);
                    if (!string.IsNullOrEmpty(account.Wallet.PrivateKeyWIF))
                    {
                        var contract = await AnyHedge.CreateContract(account.Wallet.Address, account.Wallet.PrivateKeyWIF,
                            bestContractParameters.Value.amount * latestPrice * oracleMetadata.ATTESTATION_SCALING,
                            account.OracleKey,
                            bestContractParameters.Value.premiumDataItem.Item.DurationSeconds);
                        results.Add($"{suggestedParameters}{Environment.NewLine}{contract}");
                    }
                }
            }

            return results;
        }

        internal class PremiumDataItemPlus
        {
            public PremiumDataItem Item;
            public decimal? ApyPlusPriceDelta;
            public decimal? YieldPlusPriceDeltaAnnualized;

            public PremiumDataItemPlus(PremiumDataItem item, decimal? priceDelta)
            {
                this.Item = item;
                if (priceDelta.HasValue)
                {
                    this.ApyPlusPriceDelta = (decimal)item.Apy + priceDelta;
                    this.YieldPlusPriceDeltaAnnualized = (decimal?)Premiums.YieldToApy((item.Yield + (double)priceDelta.Value) / 100, item.DurationDays);
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

            return totalCost / (walletBalance - remainingBalance);
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

            Widgets.DisplayTable(rows, borders: false);
        }

        private static void DisplayPremiumsData(List<PremiumDataItemPlus> premiumData, decimal? priceDelta)
        {
            List<string> headers = ["Amount (BCH)", "Duration (days)", "Yield (%)", "APY (%)"];
            switch (priceDelta)
            {
                case > 0:
                    headers.Add("APY + Δ (%)");
                    break;
                case < 0:
                    headers.Add("AP(Y+Δ) (%)");
                    break;
            }

            List<List<string>> rows = [headers];

            foreach (var item in premiumData)
            {
                List<string> row = [
                    item.Item.Amount.ToString(),
                    item.Item.DurationDays.ToString(),
                    item.Item.Yield.ToString("F2"),
                    item.Item.Apy.ToString("F2"),
                ];
                
                switch (priceDelta)
                {
                    case > 0:
                        row.Add(item.ApyPlusPriceDelta.Format(2, 0));
                        break;
                    case < 0:
                        row.Add(item.YieldPlusPriceDeltaAnnualized.Format(2, 0));
                        break;
                }
                
                rows.Add(row);
            }

            Widgets.DisplayTable(rows, borders: false);
        }

        private static (decimal amount, PremiumDataItemPlus premiumDataItem)? GetBestContractParameters_MaxAmount(List<PremiumDataItemPlus> premiumData, decimal walletBalanceBch)
        {
            var candidates = premiumData
                .GroupBy(x => x.Item.Amount)
                .ToList();

            var candidatesThatFitWholeBalance = candidates.Where(x => x.Key >= walletBalanceBch).ToList();
            PremiumDataItemPlus? bestCandidate;
            if (candidatesThatFitWholeBalance.Any())
            {
                bestCandidate = candidatesThatFitWholeBalance.OrderBy(x => x.Key).First().OrderBy(x => x.Item.Apy).Last();
                return (walletBalanceBch, bestCandidate);
            }

            bestCandidate = candidates.OrderBy(x => x.Key).Last().OrderBy(x => x.Item.Apy).Last();
            return (bestCandidate.Item.Amount, bestCandidate);
        }

        internal static (decimal amount, PremiumDataItemPlus premiumDataItem)? GetBestContractParameters_MaxApy(List<PremiumDataItemPlus> premiumData, decimal walletBalanceBch)
        {
            var bestCandidate = premiumData.MaxBy(x=>x.Item.Apy)!;

            var upgradeCandidate = premiumData
                .Where(x =>
                    x.Item.BestApyForAmount &&
                    x.Item.DurationDays == bestCandidate.Item.DurationDays &&
                    x.Item.Amount > bestCandidate.Item.Amount)
                .MaxBy(x => x.Item.Amount);

            if (upgradeCandidate != null)
            {
                bestCandidate = upgradeCandidate;
            }

            return (Math.Min(bestCandidate.Item.Amount, walletBalanceBch), bestCandidate);
        }
    }
}