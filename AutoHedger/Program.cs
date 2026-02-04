using System.Text;
using AnyHedgeNet;
using ConsoleWidgets;
using OraclesCash;
using Timer = System.Timers.Timer;


/*
 * new deposits are to be made on https://app.bchbull.com/ (not by depositing to payout address), to keep the "FIFO cost" and "Original deposit" correct
 */

//todo
/*
 * show also long premiums?
 * move the fees subtraction to the AnyHedgeNet.dll
 */

namespace AutoHedger
{
    class Program
    {
        private static Timer timer;
        private static TimeSpan TimerMinutes = TimeSpan.FromMinutes(3);

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
                Console.Write("Fetched");
                
                var tasks = new List<Task>(4);
                var transactionsTask = AnyHedge.GetTransactions();
                var contractsTask = transactionsTask.ContinueWith(task =>
                {
                    Console.Write(" ..Transactions");
                    return AnyHedge.GetContracts(task.Result.Select(x=>x.ContractAddress).ToList());
                }).Unwrap();
                tasks.Add(contractsTask.ContinueWith(_ => Console.Write(" ..Contracts")));
                const string counterLeverage = "5"; //only check 20% hedge
                var premiumDataTask = Premiums.GetPremiums(counterLeverage, 5);
                tasks.Add(premiumDataTask.ContinueWith(_ => Console.Write(" ..Premiums")));
                tasks.Add(TermedDepositAccount.UpdateLatestPrices(accounts).ContinueWith(_ => Console.Write(" ..Latest prices")));
                tasks.Add(TermedDepositAccount.UpdateWalletBalances(accounts).ContinueWith(_ => Console.Write(" ..Wallet balances")));
                
                await Task.WhenAll(tasks);
                Console.WriteLine(" ..DONE");
                    
                var transactions = await transactionsTask;
                var contracts = await contractsTask;
                var premiumData = await premiumDataTask;

                foreach (var account in accounts)
                {
                    Console.WriteLine(delimiterBold);
                    var takerContractProposal = await DisplayData(account, transactions, contracts, premiumData.Where(x => x.CurrencyOracleKey == account.OracleKey).ToList());
                    if (takerContractProposal != null && !string.IsNullOrEmpty(account.Wallet.PrivateKeyWIF))
                    {
                        if (takerContractProposal.account.Wallet.AutoMode)
                        {
                            DisplayContractProposal(takerContractProposal);
                            return;
                        }
                        contractProposals.Add(takerContractProposal);
                    }
                }

                if (accounts.Length > 1)
                {
                    Console.WriteLine(delimiterBold);
                    DisplayTotalBalances(accounts);
                    
                    var activeContracts = contracts.Where(x => !x.IsSettled).ToList();
                    if (activeContracts.Count > 0)
                    {
                        Console.WriteLine();
                        Console.WriteLine($"Total active contracts: {activeContracts.Count}");

                        var contractWithNearestMaturity = activeContracts.MinBy(x => x.Parameters.MaturityTimestamp);
                        var maturityUtc = DateTimeOffset.FromUnixTimeSeconds((long)contractWithNearestMaturity.Parameters.MaturityTimestamp).UtcDateTime;
                        Console.WriteLine($"Nearest contract maturity: {maturityUtc} ({(maturityUtc - DateTime.UtcNow).TotalDays:F2} days)");
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
            
            const string logHeader = "datetime\tBCH\tamount\tasset\tdays\tyield\tApyDeltaAdjusted\tresult";
            List<object> logItems = [];
            
            try
            {
                Console.Clear();
                Console.WriteLine(DateTime.Now);
                Console.WriteLine(proposal);

                var makerContractPoposal = await AnyHedge.ProposeContract(proposal.account.Wallet.Address, proposal.account.Wallet.PrivateKeyWIF,
                    proposal.contractAmountBch * proposal.account.LatestPrice * proposal.account.OracleMetadata.ATTESTATION_SCALING,
                    proposal.account.OracleKey,
                    proposal.bestPremiumDataItem.Item.DurationSeconds);

                Console.WriteLine();
                var amountBch = (decimal)makerContractPoposal.Metadata.ShortInputInSatoshis / _satsPerBch;
                var amount = makerContractPoposal.Metadata.NominalUnits / proposal.account.OracleMetadata.ATTESTATION_SCALING;
                var days = Math.Round(TimeSpan.FromSeconds((double)makerContractPoposal.Metadata.DurationInSeconds).TotalDays, 3);
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

                decimal? acquisitionCostFifo = proposal.account.BchAcquisitionCostFifo;
                decimal? priceDelta = (startPrice - acquisitionCostFifo) / acquisitionCostFifo * 100;
                PremiumDataItemPlus premiumDataItemPlus = new(new PremiumDataItem(amountBch, days, yield), priceDelta);
                var apyPriceDeltaAdjusted = premiumDataItemPlus.ApyPriceDeltaAdjusted;
                
                List<List<string>> rows =
                [
                    ["", "Suggested", "LP proposal", "LP proposal %"],
                    [
                        "Size:                  ",
                        $"{proposal.contractAmountBch.Format()} BCH",
                        $"{amountBch.Format()} BCH",
                        $"{(amountBch / proposal.contractAmountBch * 100).Format(2)}"
                    ],
                    //["Days:          ", $"{proposal.bestPremiumDataItem.Item.DurationDays}", days.ToString()],
                    [
                        "Price:                 ",
                        $"{proposal.account.LatestPrice.Format()} {proposal.account.Wallet.Currency}",
                        $"{startPrice.Format()} {proposal.account.Wallet.Currency}".ToString(),
                        $"{(startPrice / proposal.account.LatestPrice * 100).Format(2)}"
                    ],
                    //["Total fee:     ", $"{0}", $"{totalFeeBch.Format()} BCH"],
                    [
                        "LP fee:                ",
                        $"{proposal.bestPremiumDataItem.Item.PremiumInfo.LiquidityPremium.Format(3)} %",
                        $"{liquidityFee} SAT",
                        ""
                    ],
                    [
                        "SS fee:                ",
                        $"{proposal.bestPremiumDataItem.Item.PremiumInfo.SettlementServiceFee.Format(3)} %",
                        $"{settlementFee} SAT",
                        ""
                    ],
                    [
                        "Yield:                 ",
                        $"{proposal.bestPremiumDataItem.Item.Yield.Format(3)} %",
                        $"{yield.Format(3)} %",
                        $"{(proposal.bestPremiumDataItem.Item.Yield == 0 ? "N/A" : (proposal.bestPremiumDataItem.Item.Yield < 0 && yield < 0 ?
                            proposal.bestPremiumDataItem.Item.Yield / yield * 100 :
                            yield / proposal.bestPremiumDataItem.Item.Yield * 100)
                            .Format(2))}"
                    ],
                    [
                        "APY, price Δ adjusted: ",
                        $"{proposal.bestPremiumDataItem.ApyPriceDeltaAdjusted.Format(3)} %",
                        $"{apyPriceDeltaAdjusted.Format(3)} %",
                        $"{(proposal.bestPremiumDataItem.ApyPriceDeltaAdjusted == 0 ? "N/A" : (apyPriceDeltaAdjusted / proposal.bestPremiumDataItem.ApyPriceDeltaAdjusted * 100).Format(2))}"
                    ],
                ];

                Widgets.DisplayTable(rows);

                Console.WriteLine();
                string? answer = null;
                var autoMode = proposal.account.Wallet.AutoMode;
                var worseYieldThanExpected = Math.Round(yield, 2) < proposal.bestPremiumDataItem.Item.Yield || apyPriceDeltaAdjusted < AppSettings.MinimumApy;
                if (worseYieldThanExpected)
                {
                    proposal.account.StalePremiumsTimestamp = proposal.bestPremiumDataItem.Item.Timestamp;
                }
                
                if (!autoMode)
                {
                    Console.WriteLine("To fund the contract type 'yes'.");
                    Console.WriteLine("Any other answer returns to main screen.");
                    answer = Console.ReadLine();
                }
                else if (worseYieldThanExpected)
                {
                    Console.WriteLine("Returns to main screen in 10s ..");
                    await Task.Delay(TimeSpan.FromSeconds(10));                    
                }
                else
                {
                    const int delay = 20;
                    Console.WriteLine($"Autofunded in {delay}s ..");
                    await Task.Delay(TimeSpan.FromSeconds(delay));
                }

                if (autoMode && !worseYieldThanExpected ||
                    answer?.ToUpper() == "YES")
                {
                    logItems =
                    [
                        DateTime.Now,
                        amountBch.Format(),
                        amount.Format(proposal.account.OracleMetadata.AssetDecimals, 0),
                        proposal.account.Wallet.Currency,
                        days.Format(1, 0),
                        $"{yield.Format(3, 0)}%",
                        $"{apyPriceDeltaAdjusted.Format(3, 0)}%",
                    ];
                    
                    Console.WriteLine("Funding contract ..");
                    var result = await AnyHedge.FundContract(proposal.account.Wallet.Address, proposal.account.Wallet.PrivateKeyWIF,
                        proposal.contractAmountBch * proposal.account.LatestPrice * proposal.account.OracleMetadata.ATTESTATION_SCALING,
                        proposal.account.OracleKey,
                        proposal.bestPremiumDataItem.Item.DurationSeconds,
                        makerContractPoposal);
                    Console.WriteLine(result);
                    
                    logItems.Add(result.Trim());
                    TxLog.Log(logHeader, logItems);
                    
                    if (!autoMode)
                    {
                        Console.WriteLine("[Enter] returns to main screen.");
                        Console.ReadLine();
                    }
                }
            }
            catch (Exception e)
            {
                Widgets.WriteLine($"Something went wrong {e}", ConsoleColor.Red);
                
                logItems.Add($"ERROR: {e.GetType().Name}: {e.Message}");
                TxLog.Log(logHeader, logItems);
                
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

        private static async Task<TakerContractProposal?> DisplayData(TermedDepositAccount account, List<AnyHedgeManager.TxMetadata> transactions,
            List<Contract> contracts, List<PremiumDataItem> premiumData)
        {
            decimal? walletBalanceBch = account.WalletBalanceBch;
            decimal? walletBalance = account.WalletBalance;
            
            account.UpdateContractInfo(transactions, contracts);
            decimal? contractsBalanceBch = account.ContractsBalanceBch;
            decimal? contractsBalance = account.ContractsBalance;
            decimal? bchAcquisitionCostFifo = account.BchAcquisitionCostFifo;

            Console.WriteLine($"BCH acquisition cost FIFO: {bchAcquisitionCostFifo.Format(8, 24)} {account.Wallet.Currency}");
            var priceDelta = (account.LatestPrice - bchAcquisitionCostFifo) / bchAcquisitionCostFifo * 100;
            Console.WriteLine($"Latest price from OraclesCash: {account.LatestPrice,20:N8} {account.Wallet.Currency} (Δ {priceDelta.Format(2, 0, true)} %)");

            if (walletBalanceBch.HasValue || contractsBalanceBch != 0)
            {
                Console.WriteLine(delimiter);
                DisplayBalances(walletBalanceBch, walletBalance, contractsBalanceBch, contractsBalance, account.OracleMetadata, account.Wallet);
            }

            if (account.OriginalDeposit > 0)
            {
                Console.WriteLine(delimiter);
                Console.WriteLine($"Original deposit: {account.OriginalDeposit.Format(account.OracleMetadata.AssetDecimals)} {account.Wallet.Currency}");
                var totalBalance = walletBalance + contractsBalance;
                var yield = totalBalance - account.OriginalDeposit; 
                var yieldPercent = (totalBalance / account.OriginalDeposit - 1) * 100;
                Console.WriteLine($"Yield:            {yield.Format(account.OracleMetadata.AssetDecimals)} {account.Wallet.Currency} ({yieldPercent.Format(2, 0)} %)");
            }

            var premiumDataPlus = premiumData
                .Select(x => new PremiumDataItemPlus(x, priceDelta))
                .OrderBy(x => x.Item.DurationDays)
                .ToList();

            var premiumDataFiltered = premiumDataPlus
                .Where(x => walletBalanceBch > AppSettings.MinimumContractSizeBch && x.ApyPriceDeltaAdjusted >= AppSettings.MinimumApy)
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

            if (premiumData.First().Timestamp == account.StalePremiumsTimestamp)
            {
                Console.WriteLine($"Stale premiums (or less than min desired APY) timestamp: {account.StalePremiumsTimestamp}");
                return null;
            }

            return GetTakerContractProposal(account, premiumDataFiltered);
        }

        private static TakerContractProposal? GetTakerContractProposal(TermedDepositAccount account, List<PremiumDataItemPlus> premiumDataFiltered)
        {
            decimal? walletBalanceBch = account.WalletBalanceBch;

            TakerContractProposal? takerContractProposal = null;
            if (walletBalanceBch > AppSettings.MinimumContractSizeBch && premiumDataFiltered.Any())
            {
                var bestContractParameters = GetBestContractParameters_MaxApy(premiumDataFiltered, walletBalanceBch.Value);
                if (bestContractParameters.HasValue)
                {
                    Console.WriteLine(delimiter);

                    var contractAmountBch = bestContractParameters.Value.amount;

                    var premiumInfo = bestContractParameters.Value.premiumDataItem.Item.PremiumInfo;
                    var feeMultiplier = 1m + premiumInfo.SettlementServiceFee / 100m;
                    if (premiumInfo.LiquidityPremium > 0)
                    {
                        feeMultiplier += premiumInfo.LiquidityPremium / 100m;
                    }
                    const decimal additionalFeeBch = 0.000_010_00m; //miner fees
                    
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
                    this.YieldPlusPriceDeltaAnnualized = Premiums.YieldToApy(item.Yield + priceDelta.Value, item.DurationDays);

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

        private static void DisplayBalances(decimal? walletBalanceBch, decimal? walletBalance, decimal? contractsBalanceBch, decimal? contractsBalance,
            OracleMetadata oracleMetadata, WalletConfig wallet)
        {
            int assetDecimals = oracleMetadata.AssetDecimals;

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
        
        private static void DisplayTotalBalances(TermedDepositAccount[] accounts)
        {
            decimal? walletBalanceBch = accounts.Sum(a => a.WalletBalanceBch);
            decimal? contractsBalanceBch = accounts.Sum(a => a.ContractsBalanceBch);
            
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
                ["", "BCH", "%"],
                ["Total wallet balance:          ", walletBalanceBch.Format(), walletPercent.Format(2, 7)],
                ["Total active contracts balance:", contractsBalanceBch.Format(), contractsPercent.Format(2, 7)],
                ["Total balance:                 ", totalBch.Format(), ""]
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
            var bestApyCandidate = premiumData.MaxBy(x => x.ApyPriceDeltaAdjusted)!;
            
            var bestApyForAmount = premiumData
                .GroupBy(item => item.Item.Amount)
                .Select(group => group.OrderByDescending(item => item.ApyPriceDeltaAdjusted).First())
                .ToDictionary(item => item.Item.Amount, item => item.ApyPriceDeltaAdjusted);
            
            foreach (var item in premiumData)
            {
                item.Item.BestApyForAmount = item.ApyPriceDeltaAdjusted == bestApyForAmount[item.Item.Amount];
            }

            var candidates = premiumData
                .Where(x =>
                    x.Item.BestApyForAmount &&
                    x.Item.DurationDays == bestApyCandidate.Item.DurationDays &&
                    x.Item.Amount >= bestApyCandidate.Item.Amount)
                .OrderBy(x => x.Item.Amount)
                .ToList();

            var bestCandidate = candidates.FirstOrDefault(x => x.Item.Amount >= walletBalanceBch) ?? candidates.Last();

            return (Math.Min(bestCandidate.Item.Amount, walletBalanceBch), bestCandidate);
        }
    }
}