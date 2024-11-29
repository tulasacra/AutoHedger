using AnyHedgeNet;
using Microsoft.Extensions.Configuration;
using NBitcoin;

namespace AutoHedger
{
    public static class AppSettings
    {
        private static readonly IConfiguration _config;

        static AppSettings()
        {
            _config = new ConfigurationBuilder()
                .AddJsonFile(Path.Combine("Settings", "appsettings.json"), optional: false)
                .Build();
        }

        public static decimal MinimumApy => decimal.Parse(_config["MinimumApy"]);
        public static decimal MinimumContractSizeBch => decimal.Parse(_config["MinimumContractSizeBch"]);
        public static string AccountKey => _config["AccountKey"];

        public static List<WalletConfig> Wallets => _config.GetSection("Wallets")
            .GetChildren()
            .Select(x => new WalletConfig
            {
                Currency = Enum.Parse<Currency>(x["Currency"]),
                Address = x["Address"],
                PrivateKeyWIF = x["PrivateKeyWIF"]
            })
            .ToList();
    }

    public class WalletConfig
    {
        private string _privateKeyWIF;
        private string _address;

        public Currency Currency { get; set; }

        public string Address
        {
            get => _address;
            set => _address = value;
        }

        public string PrivateKeyWIF
        {
            get => _privateKeyWIF;
            set
            {
                _privateKeyWIF = value;
                if (!HasAddress && !string.IsNullOrEmpty(value))
                {
                    var key = new BitcoinSecret(value, Network.Main);
                    //todo cashaddress _address = key.GetAddress(ScriptPubKeyType.Legacy).ToString();
                }
            }
        }

        public bool HasAddress => !string.IsNullOrEmpty(_address) && _address != "bitcoincash:";
    }
}