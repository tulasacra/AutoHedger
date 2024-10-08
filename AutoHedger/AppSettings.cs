using Microsoft.Extensions.Configuration;

namespace AutoHedger
{
    public static class AppSettings
    {
        private static readonly IConfiguration _config;
        
        static AppSettings()
        {
            _config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false)
                .Build();
        }

        public static double MinimumApy => double.Parse(_config["MinimumApy"]);
        public static string AccountKey => _config["AccountKey"];
        public static List<WalletConfig> Wallets => _config.GetSection("Wallets")
            .GetChildren()
            .Select(x => new WalletConfig
            {
                Currency = Enum.Parse<Currency>(x["Currency"]),
                Address = x["Address"]
            })
            .ToList();
    }

    public class WalletConfig
    {
        public Currency Currency { get; set; }
        public string Address { get; set; }
    }
}