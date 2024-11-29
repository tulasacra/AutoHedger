using System.Text;
using AnyHedgeNet;
using ConsoleWidgets;
using OraclesCash;
using Timer = System.Timers.Timer;


//todo
/*
 * reset refresh timer after manual reset
 * show longs in main
 * move the fees subtraction to the ah.dll
 * improve fee estimate by looking at current settlement fees
 * check yield and latest price diffs between main and propose
 * check what happens if new deposits are made in the payout address instead of new contract 
 */

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

            timer = new Timer(TimeSpan.FromMinutes(Minutes));
            timer.Elapsed += async (sender, e) => Refresh();
            timer.AutoReset = true;
            timer.Enabled = true;

            Menu.AddOption(ConsoleKey.R, "Refresh", Refresh);

            Refresh();
            await Menu.Start();
        }

        private static async Task DisplayData()
        {
            Console.Clear();
            Console.WriteLine($"Checking at: {DateTime.Now}");
            Console.WriteLine($"Minimum desired APY: {AppSettings.MinimumApy} %");

            var contractProposals = new List<TakerContractProposal>();
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

                Console.Write("Reading latest prices ..");
                await TermedDepositAccount.UpdateLatestPrices(accounts);
                Console.WriteLine("OK");
                
                Console.Write("Reading wallet balances ..");
                try
                {
                    TermedDepositAccount.UpdateWalletBalances(accounts);
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
                Widgets.WriteLine($"Something went wrong (retrying in {Minutes} minutes): {ex}", ConsoleColor.Red);
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
            Menu.Disable();
            try
            {
                Console.Clear();
                Console.WriteLine(proposal);
			
                var makerContractPoposal = await AnyHedge.ProposeContract(proposal.account.Wallet.Address, proposal.account.Wallet.PrivateKeyWIF,
                    proposal.contractAmountBch * proposal.account.LatestPrice * proposal.account.OracleMetadata.ATTESTATION_SCALING,
                    proposal.account.OracleKey,
                    proposal.bestPremiumDataItem.Item.DurationSeconds);
            
                Console.WriteLine(makerContractPoposal.Metadata.ShortInputInSatoshis);
                Console.WriteLine(makerContractPoposal.Metadata.DurationInSeconds);
                Console.WriteLine(makerContractPoposal.Fees[0].Satoshis);
                Console.WriteLine(makerContractPoposal.Fees[1].Satoshis);
                Console.WriteLine(makerContractPoposal.Metadata.StartPrice);
            
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
                }
            }
            catch(Exception e)
            {
                Widgets.WriteLine($"Something went wrong {e}", ConsoleColor.Red);
            }
            finally
            {
                Console.WriteLine("[Enter] returns to main screen.");
                Console.ReadLine();
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

            if (walletBalanceBch != null || contractsBalanceBch != 0)
            {
                Console.WriteLine(delimiter);
                DisplayBalances(walletBalanceBch, walletBalance, contractsBalanceBch, contractsBalance, oracleMetadata, account.Wallet);
            }

            var premiumDataPlus = premiumData
                .Select(x => new PremiumDataItemPlus(x, priceDelta))
                .Where(x => x.ApyPriceDeltaAdjusted >= (decimal)AppSettings.MinimumApy)
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

            TakerContractProposal? takerContractProposal = null;
            if (walletBalanceBch.HasValue && premiumDataPlus.Any())
            {
                var bestContractParameters = GetBestContractParameters_MaxApy(premiumDataPlus, walletBalanceBch.Value);
                if (bestContractParameters.HasValue)
                {
                    Console.WriteLine(delimiter);

                    var contractAmountBch = bestContractParameters.Value.amount;
                    
                    //todo better fees estimation
                    const decimal feeMultiplier = 1.05m; //5%
                    if (contractAmountBch * feeMultiplier > walletBalanceBch)
                    {
                        contractAmountBch /= feeMultiplier;
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
            // APY+Δ
            public decimal? ApyPlusPriceDelta;
            // AP(Y+Δ)
            public decimal? YieldPlusPriceDeltaAnnualized;
            public decimal ApyPriceDeltaAdjusted;

            public PremiumDataItemPlus(PremiumDataItem item, decimal? priceDelta)
            {
                this.Item = item;
                if (priceDelta.HasValue)
                {
                    this.ApyPlusPriceDelta = (decimal)item.Apy + priceDelta;
                    this.YieldPlusPriceDeltaAnnualized = (decimal?)Premiums.YieldToApy((item.Yield + (double)priceDelta.Value) / 100, item.DurationDays);
                    
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
                    this.ApyPriceDeltaAdjusted = (decimal)item.Apy;
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