namespace AutoHedger.Tests;

public class Tests
{
    [SetUp]
    public void Setup()
    {
    }

    private static void AssertContractParameters((decimal amount, Program.PremiumDataItemPlus premiumDataItem)? result, decimal expectedAmount, int expectedDurationDays)
    {
        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.amount, Is.EqualTo(expectedAmount));
            Assert.That(result.Value.premiumDataItem.Item.DurationDays, Is.EqualTo(expectedDurationDays));
        });
    }

    private List<Program.PremiumDataItemPlus> premiumData =
    [
        new(new PremiumDataItem { Amount = 1m, DurationDays = 30, Apy = 14.10, BestApyForAmount = true }, null),
        new(new PremiumDataItem { Amount = 10m, DurationDays = 60, Apy = 28.21, BestApyForAmount = true }, null),
        new(new PremiumDataItem { Amount = 100m, DurationDays = 90, Apy = 21.04, BestApyForAmount = true }, null)
    ];

    [Test]
    public void GetBestContractParameters_MaxApy_SmallerAmount()
    {
        decimal walletBalanceBch = 15m;

        var result = Program.GetBestContractParameters_MaxApy(premiumData, walletBalanceBch);
        AssertContractParameters(result, 10m, 60);
    }

    [Test]
    public void GetBestContractParameters_MaxApy_ExactAmount()
    {
        decimal walletBalanceBch = 1.5m;

        var result = Program.GetBestContractParameters_MaxApy(premiumData, walletBalanceBch);
        AssertContractParameters(result, 1.5m, 60);
    }

    [Test]
    public void GetBestContractParameters_MaxApy_SameDurationBiggerAmountUpgradePossible()
    {
        decimal walletBalanceBch = 40m;

        List<Program.PremiumDataItemPlus> premiumData =
        [
            new(new PremiumDataItem { Amount = 1m, DurationDays = 30, Apy = 14.10, BestApyForAmount = true }, null),
            new(new PremiumDataItem { Amount = 5m, DurationDays = 60, Apy = 28.21, BestApyForAmount = true }, null),
            new(new PremiumDataItem { Amount = 10m, DurationDays = 60, Apy = 28, BestApyForAmount = true }, null),
            new(new PremiumDataItem { Amount = 50m, DurationDays = 60, Apy = 20.21, BestApyForAmount = true }, null),
            new(new PremiumDataItem { Amount = 100m, DurationDays = 60, Apy = 20, BestApyForAmount = true }, null),
            new(new PremiumDataItem { Amount = 500m, DurationDays = 90, Apy = 1.04, BestApyForAmount = true }, null),
        ];

        var result = Program.GetBestContractParameters_MaxApy(premiumData, walletBalanceBch);
        AssertContractParameters(result, 40m, 60);
    }
    
    [Test]
    public void GetBestContractParameters_MaxApy_SameDurationBiggerAmountUpgradeNotPossible()
    {
        // usually there is a single duration that has the best apy for every amount
        // but sometimes other durations can offer higher apy for some of the amounts

        decimal walletBalanceBch = 150m;

        List<Program.PremiumDataItemPlus> premiumData =
        [
            new(new PremiumDataItem { Amount = 1m, DurationDays = 30, Apy = 14.10, BestApyForAmount = true }, null),
            new(new PremiumDataItem { Amount = 10m, DurationDays = 60, Apy = 28.21, BestApyForAmount = true }, null),
            new(new PremiumDataItem { Amount = 50m, DurationDays = 60, Apy = 20.21, BestApyForAmount = false }, null),
            new(new PremiumDataItem { Amount = 10m, DurationDays = 90, Apy = 24.04, BestApyForAmount = false }, null),
            new(new PremiumDataItem { Amount = 50m, DurationDays = 90, Apy = 21.04, BestApyForAmount = true }, null),
        ];

        var result = Program.GetBestContractParameters_MaxApy(premiumData, walletBalanceBch);
        AssertContractParameters(result, 10m, 60);
    }
}