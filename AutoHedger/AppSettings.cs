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

        public static Currency Currency => Enum.Parse<Currency>(_config["Currency"]);
        public static double MinimumApy => double.Parse(_config["MinimumApy"]);
        public static string AccountKey => _config["AccountKey"];
        public static string WalletAddress => _config["WalletAddress"];
    }
}