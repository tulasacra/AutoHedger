using System.Text;
using AnyHedgeNet;
using ConsoleWidgets;
using OraclesCash;
using Timer = System.Timers.Timer;


//todo
/*
 * show longs in main
 * move the fees subtraction to the ah.dll
 * check what happens if new deposits are made in the payout address instead of new contract 
 */

namespace AutoHedger
{
    class Program
    {
        private static Timer timer;
        private static TimeSpan TimerMinutes = TimeSpan.FromMinutes(15);

        private const decimal _satsPerBch = 100_000_000m;
        private const string delimiter = "-------------------------------------------------------------------------------------";
        private static readonly string delimiterBold = $"{Environment.NewLine}===================================================================================================={Environment.NewLine}";
        private static AnyHedgeManager AnyHedge;
        private static Menu Menu = new();
        private static TermedDepositAccount[] accounts;


        static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            AnyHedge = new AnyHedgeManager(AppSettings.AccountKey);

            Console.Write("Reading OracleMetadata ..");
            accounts = await TermedDepositAccount.Get(AppSettings.Wallets);

            async void Refresh()
            {
                await DisplayData();
            }

            timer = new Timer();
            timer.Elapsed += async (sender, e) => Refresh();
            timer.AutoReset = true;

            Menu.AddOption(ConsoleKey.R, "Refresh", Refresh);

            Refresh();
            await Menu.Start();
        }

        private static async Task DisplayData()
        {
            timer.Interval = TimerMinutes.TotalMilliseconds;
            timer.Start();

            Console.Clear();
            Console.WriteLine($"Checking at: {DateTime.Now}");
            Console.WriteLine($"Minimum desired APY: {AppSettings.MinimumApy} %");
            Console.WriteLine($"Minimum contract size: {AppSettings.MinimumContractSizeBch} BCH");

            var contractProposals = new List<TakerContractProposal>();
            try
            {
                Console.Write("Reading contracts ..");
                var contractAddresses = await AnyHedge.GetContractAddresses();
                var contracts = await AnyHedge.GetContracts(contractAddresses);
                Console.WriteLine("OK");

                Console.Write("Reading premiums ..");
                const string counterLeverage = "5"; //only check 20% hedge
                var premiumData = (await Premiums.GetPremiums(counterLeverage, 5)).ToList();
                Console.WriteLine("OK");

                Console.Write("Reading latest prices ..");
                await TermedDepositAccount.UpdateLatestPrices(accounts);
                Console.WriteLine("OK");

                Console.Write("Reading wallet balances ..");
                try
                {
                    await TermedDepositAccount.UpdateWalletBalances(accounts);
                    Console.WriteLine("OK");
                }
                catch (Exception ex)
                {
                    Widgets.WriteLine($"Error getting wallet balance: {ex.Message}", ConsoleColor.Red);
                }

                foreach (var account in accounts)
                {
                    Console.WriteLine(delimiterBold);
                    var takerContractProposal = await DisplayData(account, contracts, premiumData.Where(x => x.CurrencyOracleKey == account.OracleKey).ToList());
                    if (takerContractProposal != null && !string.IsNullOrEmpty(account.Wallet.PrivateKeyWIF))
                    {
                        contractProposals.Add(takerContractProposal);
                    }
                }
            }
            catch (Exception ex)
            {
                Widgets.WriteLine($"Something went wrong (retrying in {TimerMinutes.TotalMinutes} minutes): {ex}", ConsoleColor.Red);
            }

            #region Add proposals to menu

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

            #endregion

            Console.WriteLine(delimiterBold);
            Menu.Show();
        }

        private static async Task DisplayContractProposal(TakerContractProposal proposal)
        {
            timer.Stop();
            Menu.Disable();
            try
            {
                Console.Clear();
                Console.WriteLine(proposal);

                var makerContractPoposal = await AnyHedge.ProposeContract(proposal.account.Wallet.Address, proposal.account.Wallet.PrivateKeyWIF,
                    proposal.contractAmountBch * proposal.account.LatestPrice * proposal.account.OracleMetadata.ATTESTATION_SCALING,
                    proposal.account.OracleKey,
                    proposal.bestPremiumDataItem.Item.DurationSeconds);

                Console.WriteLine();
                var amountBch = (decimal)makerContractPoposal.Metadata.ShortInputInSatoshis / _satsPerBch;
                //var days = Math.Round(TimeSpan.FromSeconds((double)makerContractPoposal.Metadata.DurationInSeconds).TotalDays, 3);
                var liquidityFeeMultiplier = 1;
                if (makerContractPoposal.Fees[0].Address == proposal.account.Wallet.Address)
                {
                    liquidityFeeMultiplier = -1;
                }
                var liquidityFee = liquidityFeeMultiplier * makerContractPoposal.Fees[0].Satoshis;
                var settlementFee = makerContractPoposal.Fees[1].Satoshis;
                var totalFeeBch = (decimal)(liquidityFee + settlementFee) / _satsPerBch;
                var yield = -totalFeeBch / amountBch * 100;
                var startPrice = (decimal)makerContractPoposal.Metadata.StartPrice / proposal.account.OracleMetadata.ATTESTATION_SCALING;
                List<List<string>> rows =
                [
                    ["", "Suggested", "LP proposal", "LP proposal %"],
                    [
                        "Size:     ",
                        $"{proposal.contractAmountBch} BCH",
                        $"{amountBch.Format()} BCH",
                        $"{(amountBch / proposal.contractAmountBch * 100).Format(2)}"
                    ],
                    //["Days:          ", $"{proposal.bestPremiumDataItem.Item.DurationDays}", days.ToString()],
                    [
                        "Price:    ",
                        $"{proposal.account.LatestPrice.Format()} {proposal.account.Wallet.Currency}",
                        $"{startPrice.Format()} {proposal.account.Wallet.Currency}".ToString(),
                        $"{(startPrice / proposal.account.LatestPrice * 100).Format(2)}"
                    ],
                    //["Total fee:     ", $"{0}", $"{totalFeeBch.Format()} BCH"],
                    [
                        "Yield:    ",
                        $"{proposal.bestPremiumDataItem.Item.Yield.Format(2)} %",
                        $"{yield.Format(2)} %",
                        $"{(yield / proposal.bestPremiumDataItem.Item.Yield * 100).Format(2)}"
                    ],
                ];

                Widgets.DisplayTable(rows);

                Console.WriteLine();
                Console.WriteLine("To fund the contract type 'yes'.");
                Console.WriteLine("Any other answer returns to main screen.");
                var answer = Console.ReadLine();
                if (answer.ToUpper() == "YES")
                {
                    var result = await AnyHedge.FundContract(proposal.account.Wallet.Address, proposal.account.Wallet.PrivateKeyWIF,
                        proposal.contractAmountBch * proposal.account.LatestPrice * proposal.account.OracleMetadata.ATTESTATION_SCALING,
                        proposal.account.OracleKey,
                        proposal.bestPremiumDataItem.Item.DurationSeconds,
                        makerContractPoposal);
                    Console.WriteLine(result);
                    Console.WriteLine("[Enter] returns to main screen.");
                    Console.ReadLine();
                }
            }
            catch (Exception e)
            {
                Widgets.WriteLine($"Something went wrong {e}", ConsoleColor.Red);
                Console.WriteLine("[Enter] returns to main screen.");
                Console.ReadLine();
            }
            finally
            {
                DisplayData();
            }
        }

        class TakerContractProposal(decimal contractAmountBch, PremiumDataItemPlus bestPremiumDataItem, TermedDepositAccount account)
        {
            public TermedDepositAccount account = account;
            public decimal contractAmountBch = contractAmountBch;
            public PremiumDataItemPlus bestPremiumDataItem = bestPremiumDataItem;

            public override string ToString()
            {
                return $"Suggested contract parameters: {contractAmountBch} BCH, {bestPremiumDataItem.Item.DurationDays} days";
            }
        }

        private static async Task<TakerContractProposal?> DisplayData(TermedDepositAccount account, List<Contract> contracts, List<PremiumDataItem> premiumData)
        {
            var oracleKey = account.OracleKey;
            OracleMetadata? oracleMetadata = account.OracleMetadata;

            decimal? walletBalanceBch = account.WalletBalanceBch;
            decimal? walletBalance = account.WalletBalance;


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
            contractsBalanceBch = contractsBalance / account.LatestPrice;
            bchAcquisitionCostFifo = CalculateFifoCost(walletBalanceBch, settledContracts, oracleMetadata);

            Console.WriteLine($"BCH acquisition cost FIFO: {bchAcquisitionCostFifo.Format(8, 24)} {account.Wallet.Currency}");
            var priceDelta = (account.LatestPrice - bchAcquisitionCostFifo) / bchAcquisitionCostFifo * 100;
            Console.WriteLine($"Latest price from OraclesCash: {account.LatestPrice,20:N8} {account.Wallet.Currency} (Δ {priceDelta.Format(2, 0, true)} %)");

            if (walletBalanceBch.HasValue || contractsBalanceBch != 0)
            {
                Console.WriteLine(delimiter);
                DisplayBalances(walletBalanceBch, walletBalance, contractsBalanceBch, contractsBalance, oracleMetadata, account.Wallet);
            }

            var premiumDataPlus = premiumData
                .Select(x => new PremiumDataItemPlus(x, priceDelta))
                .OrderBy(x => x.Item.DurationDays)
                .ToList();

            var premiumDataFiltered = premiumDataPlus
                .Where(x => x.ApyPriceDeltaAdjusted >= AppSettings.MinimumApy)
                .ToList();

            if (premiumDataFiltered.Any())
            {
                Console.WriteLine(delimiter);
                DisplayPremiumsData(premiumDataFiltered, priceDelta);
            }
            else
            {
                premiumDataPlus = premiumDataPlus
                    .Where(x => x.Item.Apy >= AppSettings.MinimumApy)
                    .ToList();

                if (premiumDataPlus.Any())
                {
                    Console.WriteLine(delimiter);
                    DisplayPremiumsData(premiumDataPlus, priceDelta);
                }
            }

            TakerContractProposal? takerContractProposal = null;
            if (walletBalanceBch > AppSettings.MinimumContractSizeBch && premiumDataFiltered.Any())
            {
                var bestContractParameters = GetBestContractParameters_MaxApy(premiumDataFiltered, walletBalanceBch.Value);
                if (bestContractParameters.HasValue)
                {
                    Console.WriteLine(delimiter);

                    var contractAmountBch = bestContractParameters.Value.amount;

                    var feeMultiplier = 1m + bestContractParameters.Value.premiumDataItem.Item.PremiumInfo.SettlementServiceFee / 100m;
                    const decimal additionalFeeBch = 0.000_030_00m; //miner fees
                    if (contractAmountBch * feeMultiplier + additionalFeeBch > walletBalanceBch)
                    {
                        contractAmountBch = (walletBalanceBch.Value - additionalFeeBch) / feeMultiplier;
                    }

                    contractAmountBch = Math.Round(contractAmountBch, 8);

                    takerContractProposal = new TakerContractProposal(contractAmountBch, bestContractParameters.Value.premiumDataItem, account);
                    Console.WriteLine(takerContractProposal);
                }
            }

            return takerContractProposal;
        }

        internal class PremiumDataItemPlus
        {
            public PremiumDataItem Item;
            // APY + Δ
            public decimal? ApyPlusPriceDelta;
            // AP(Y+Δ)
            public decimal? YieldPlusPriceDeltaAnnualized;
            public decimal ApyPriceDeltaAdjusted;

            public PremiumDataItemPlus(PremiumDataItem item, decimal? priceDelta)
            {
                this.Item = item;
                if (priceDelta.HasValue)
                {
                    this.ApyPlusPriceDelta = item.Apy + priceDelta;
                    this.YieldPlusPriceDeltaAnnualized = Premiums.YieldToApy((item.Yield + priceDelta.Value) / 100, item.DurationDays);

                    if (priceDelta >= 0)
                    {
                        this.ApyPriceDeltaAdjusted = ApyPlusPriceDelta.Value;
                    }
                    else
                    {
                        this.ApyPriceDeltaAdjusted = YieldPlusPriceDeltaAnnualized.Value;
                    }
                }
                else
                {
                    this.ApyPriceDeltaAdjusted = item.Apy;
                }
            }
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

        private static void DisplayBalances(decimal? walletBalanceBch, decimal? walletBalance, decimal? contractsBalanceBch, decimal? contractsBalance,
            OracleMetadata oracleMetadata, WalletConfig wallet)
        {
            int assetDecimals = oracleMetadata.ATTESTATION_SCALING.ToString().Length - 1;

            var totalBch = walletBalanceBch + contractsBalanceBch;
            decimal? walletPercent = null;
            decimal? contractsPercent = null;
            if (totalBch.HasValue && totalBch != 0)
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
                List<string> row =
                [
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
            var bestApyCandidate = premiumData.MaxBy(x => x.Item.Apy)!;

            var candidates = premiumData
                .Where(x =>
                    x.Item.BestApyForAmount &&
                    x.Item.DurationDays == bestApyCandidate.Item.DurationDays &&
                    x.Item.Amount >= bestApyCandidate.Item.Amount)
                .OrderBy(x => x.Item.Amount)
                .ToList();

            var bestCandidate = candidates.FirstOrDefault(x => x.Item.Amount >= walletBalanceBch)
                                ?? candidates.Last();

            return (Math.Min(bestCandidate.Item.Amount, walletBalanceBch), bestCandidate);
        }
    }
}