using Timer = System.Timers.Timer;

namespace AutoHedger
{
    class Program
    {
        private const string currency = OracleKeys.USD;
        private const double minimumApy = 5;

        private static Timer timer;

        static async Task Main(string[] args)
        {
            timer = new Timer(TimeSpan.FromMinutes(15));
            timer.Elapsed += async (sender, e) => await CheckPremiums();
            timer.AutoReset = true;
            timer.Enabled = true;

            await CheckPremiums();
            Console.ReadLine();
        }

        private static async Task CheckPremiums()
        {
            Console.Clear();
            Console.WriteLine($"Checking at: {DateTime.Now}");
            Console.WriteLine("----------------------------------------");

            const string counterLeverage = "5"; //only check 20% hedge
            var premiumData = await Premiums.GetPremiums(currency, counterLeverage);
            decimal? latestPrice = await OraclesCash.OraclesCash.GetLatestPrice(currency);

            DisplayPremiumData(premiumData, latestPrice);

            Console.WriteLine("----------------------------------------");
            Console.WriteLine("Press [Enter] to exit the program.");
        }

        private static void DisplayPremiumData(List<PremiumDataItem> premiumData, decimal? latestPrice)
        {
            Console.WriteLine($"Latest price from OraclesCash: {latestPrice.Value}");
            Console.WriteLine();

            Console.WriteLine("| Amount (BCH) | Duration (days) | Premium (%) | APY (%) | Status |");
            Console.WriteLine("|--------------|-----------------|-------------|---------|--------|");

            foreach (var item in premiumData)
            {
                if (item.PremiumInfo.Total >= 0) continue;

                double durationInDays = int.Parse(item.Duration) / 86400.0;
                double yield = item.PremiumInfo.Total / -100;
                double apy = (Math.Pow(1 + yield, 365 / durationInDays) - 1) * 100;
                string status = apy >= minimumApy ? "OK" : "";
                Console.WriteLine($"| {item.Amount,-12} | {durationInDays,-15:F2} | {item.PremiumInfo.Total,-11:F2} | {apy,-7:F2} | {status,-6} |");
            }
        }
    }
}