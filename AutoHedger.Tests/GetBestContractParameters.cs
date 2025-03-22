namespace AutoHedger.Tests;

public class Tests
{
    [SetUp]
    public void Setup()
    {
    }

    private static void AssertContractParameters((decimal amount, Program.PremiumDataItemPlus premiumDataItem)? result, decimal expectedAmount, int expectedDurationDays, decimal expectedApy)
    {
        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.amount, Is.EqualTo(expectedAmount));
            Assert.That(result.Value.premiumDataItem.Item.DurationDays, Is.EqualTo(expectedDurationDays));
            Assert.That(result.Value.premiumDataItem.ApyPriceDeltaAdjusted, Is.EqualTo(expectedApy));
        });
    }

    private List<Program.PremiumDataItemPlus> premiumData =
    [
        new(new PremiumDataItem { Amount = 1m, DurationDays = 30, Apy = 14.10m, BestApyForAmount = true }, null),
        new(new PremiumDataItem { Amount = 10m, DurationDays = 60, Apy = 28.21m, BestApyForAmount = true }, null),
        new(new PremiumDataItem { Amount = 100m, DurationDays = 90, Apy = 21.04m, BestApyForAmount = true }, null)
    ];

    [Test]
    public void GetBestContractParameters_MaxApy_SmallerAmount()
    {
        decimal walletBalanceBch = 15m;

        var result = Program.GetBestContractParameters_MaxApy(premiumData, walletBalanceBch);
        AssertContractParameters(result, 10m, 60, 28.21m);
    }

    [Test]
    public void GetBestContractParameters_MaxApy_ExactAmount()
    {
        decimal walletBalanceBch = 1.5m;

        var result = Program.GetBestContractParameters_MaxApy(premiumData, walletBalanceBch);
        AssertContractParameters(result, 1.5m, 60, 28.21m);
    }

    [Test]
    public void GetBestContractParameters_MaxApy_SameDurationBiggerAmountUpgradePossible()
    {
        decimal walletBalanceBch = 40m;

        List<Program.PremiumDataItemPlus> premiumData =
        [
            new(new PremiumDataItem { Amount = 1m, DurationDays = 30, Apy = 14.10m, BestApyForAmount = true }, null),
            new(new PremiumDataItem { Amount = 5m, DurationDays = 60, Apy = 28.21m, BestApyForAmount = true }, null),
            new(new PremiumDataItem { Amount = 10m, DurationDays = 60, Apy = 28m, BestApyForAmount = true }, null),
            new(new PremiumDataItem { Amount = 50m, DurationDays = 60, Apy = 20.21m, BestApyForAmount = true }, null),
            new(new PremiumDataItem { Amount = 100m, DurationDays = 60, Apy = 20m, BestApyForAmount = true }, null),
            new(new PremiumDataItem { Amount = 500m, DurationDays = 90, Apy = 1.04m, BestApyForAmount = true }, null),
        ];

        var result = Program.GetBestContractParameters_MaxApy(premiumData, walletBalanceBch);
        AssertContractParameters(result, 40m, 60, 20.21m);
    }
    
    [Test]
    public void GetBestContractParameters_MaxApy_SameDurationBiggerAmountUpgradeNotPossible()
    {
        // usually there is a single duration that has the best apy for every amount
        // but sometimes other durations can offer higher apy for some of the amounts

        decimal walletBalanceBch = 150m;

        List<Program.PremiumDataItemPlus> premiumData =
        [
            new(new PremiumDataItem { Amount = 1m, DurationDays = 30, Apy = 14.10m, BestApyForAmount = true }, null),
            new(new PremiumDataItem { Amount = 10m, DurationDays = 60, Apy = 28.21m, BestApyForAmount = true }, null),
            new(new PremiumDataItem { Amount = 50m, DurationDays = 60, Apy = 20.21m, BestApyForAmount = false }, null),
            new(new PremiumDataItem { Amount = 10m, DurationDays = 90, Apy = 24.04m, BestApyForAmount = false }, null),
            new(new PremiumDataItem { Amount = 50m, DurationDays = 90, Apy = 21.04m, BestApyForAmount = true }, null),
        ];

        var result = Program.GetBestContractParameters_MaxApy(premiumData, walletBalanceBch);
        AssertContractParameters(result, 10m, 60, 28.21m);
    }

    [Test]
    public void GetBestContractParameters_MaxApy_SmallWalletBalance()
    {
        decimal walletBalanceBch = 0.25118062m;

        List<Program.PremiumDataItemPlus> premiumData =
        [
            new(new PremiumDataItem { Amount = 1m, DurationDays = 3, Apy = 12.93m, BestApyForAmount = false }, null),
            new(new PremiumDataItem { Amount = 10m, DurationDays = 3, Apy = 4.99m, BestApyForAmount = false }, null),
            new(new PremiumDataItem { Amount = 1m, DurationDays = 7, Apy = 58.73m, BestApyForAmount = true }, null),
            new(new PremiumDataItem { Amount = 10m, DurationDays = 7, Apy = 53.88m, BestApyForAmount = true }, null),
            new(new PremiumDataItem { Amount = 100m, DurationDays = 7, Apy = 16.30m, BestApyForAmount = true }, null),
            new(new PremiumDataItem { Amount = 1m, DurationDays = 14, Apy = 25.99m, BestApyForAmount = false }, null),
            new(new PremiumDataItem { Amount = 1m, DurationDays = 30, Apy = 8.46m, BestApyForAmount = false }, null),
            new(new PremiumDataItem { Amount = 1m, DurationDays = 60, Apy = 10.53m, BestApyForAmount = false }, null),
            new(new PremiumDataItem { Amount = 1m, DurationDays = 90, Apy = 8.53m, BestApyForAmount = false }, null),
        ];

        var result = Program.GetBestContractParameters_MaxApy(premiumData, walletBalanceBch);
        AssertContractParameters(result, 0.25118062m, 7, 58.73m);
    }

    [Test]
    public void GetBestContractParameters_MaxApy_NegativeDelta()
    {
        decimal walletBalanceBch = 0.57074761m;
        decimal acquisitionCostFifo = 455.59001197m;
        decimal latestPriceFromOraclesCash = 453.73000000m;
        decimal? priceDelta = (latestPriceFromOraclesCash - acquisitionCostFifo) / acquisitionCostFifo * 100;
        
        List<Program.PremiumDataItemPlus> premiumData =
        [
            new(new PremiumDataItem { Amount = 1m, DurationDays = 14, Apy = 15.37m, BestApyForAmount = true, Yield = 0.55m }, priceDelta),
            new(new PremiumDataItem { Amount = 1m, DurationDays = 60, Apy = 12.27m, BestApyForAmount = false, Yield = 1.92m }, priceDelta),
            new(new PremiumDataItem { Amount = 10m, DurationDays = 60, Apy = 12.06m, BestApyForAmount = true, Yield = 1.89m }, priceDelta),
            new(new PremiumDataItem { Amount = 100m, DurationDays = 60, Apy = 9.87m, BestApyForAmount = true, Yield = 1.56m }, priceDelta),
            new(new PremiumDataItem { Amount = 1m, DurationDays = 90, Apy = 11.23m, BestApyForAmount = false, Yield = 2.66m }, priceDelta),
            new(new PremiumDataItem { Amount = 10m, DurationDays = 90, Apy = 11.10m, BestApyForAmount = false, Yield = 2.63m }, priceDelta),
            new(new PremiumDataItem { Amount = 100m, DurationDays = 90, Apy = 9.66m, BestApyForAmount = false, Yield = 2.30m }, priceDelta),
        ];

        var result = Program.GetBestContractParameters_MaxApy(premiumData, walletBalanceBch);
        AssertContractParameters(result, 0.57074761m, 60, 9.55710293625982m);
    }
}