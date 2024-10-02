using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoHedger
{
    public static class Premiums
    {
        private static readonly HttpClient client = new HttpClient();

        public static async Task<List<PremiumDataItem>> GetPremiums(string currency, string counterLeverage, double maximumPremium)
        {
            string url = "https://premiums.anyhedge.com/api/v2/currentPremiumsV2";
            HttpResponseMessage response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            string responseBody = await response.Content.ReadAsStringAsync();

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var allData = JsonSerializer.Deserialize<Dictionary<string, CurrencyData>>(responseBody, options);

            if (allData.TryGetValue(currency, out var currencyData))
            {
                var premiumDataItems = currencyData.TakerHedge
                    .SelectMany(amount => amount.Value
                        .SelectMany(leverage => leverage.Value
                            .Where(cl => cl.Key == counterLeverage)
                            .SelectMany(cl => cl.Value
                                .Where(x=>x.Value.Total<=maximumPremium)
                                .Select(duration => 
                                {
                                    double durationInSeconds = double.Parse(duration.Key);
                                    double durationInDays = durationInSeconds / 86400.0;
                                    double yield = duration.Value.Total / -100;
                                    double apy = (Math.Pow(1 + yield, 365 / durationInDays) - 1) * 100;

                                    return new PremiumDataItem
                                    {
                                        Amount = decimal.Parse(amount.Key),
                                        Leverage = double.Parse(leverage.Key),
                                        CounterLeverage = double.Parse(cl.Key),
                                        Duration = durationInDays,
                                        PremiumInfo = duration.Value,
                                        Apy = apy
                                    };
                                }))))
                    .ToList();

                var bestApyForAmount = premiumDataItems
                    .GroupBy(item => item.Amount)
                    .Select(group => group.OrderByDescending(item => item.Apy).First())
                    .ToDictionary(item => item.Amount, item => item.Apy);

                foreach (var item in premiumDataItems)
                {
                    item.BestApyForAmount = item.Apy == bestApyForAmount[item.Amount];
                }

                return premiumDataItems;
            }

            return new List<PremiumDataItem>();
        }
    }

    public enum Currency
    {
        USD,
        EUR,
        INR,
        CNY,
        XAU,
        XAG,
        BTC,
        ETH
    }

    public static class OracleKeys
    {
        public static readonly Dictionary<Currency, string> Keys = new Dictionary<Currency, string>
        {
            { Currency.USD, "02d09db08af1ff4e8453919cc866a4be427d7bfe18f2c05e5444c196fcf6fd2818" },
            { Currency.EUR, "02bb9b3324df889a66a57bc890b3452b84a2a74ba753f8842b06bba03e0fa0dfc5" },
            { Currency.INR, "02e82ad82eb88fcdfd02fd5e2e0a67bc6ef4139bbcb63ce0b107a7604deb9f7ce1" },
            { Currency.CNY, "030654b9598186fe4bc9e1b0490c6b85b13991cdb9a7afa34af1bbeee22a35487a" },
            { Currency.XAU, "021f8338ccd45a7790025de198a266f252ac43c95bf81d2469feff110beeac89dd" },
            { Currency.XAG, "02712c349ebb7555b17bdbbe9f7aad5a337fa4179d0680eec3f6c8d77bac9cfa79" },
            { Currency.BTC, "0245a107de5c6aabc9e7b976f26625b01474f90d1a7d11c180bec990b6938e731e" },
            { Currency.ETH, "038ab22e37cf020f6bbef40111ddc51083a936f0821de56ac01f799cf15b87904d" }
        };
    }

    public class PremiumDataItem
    {
        public decimal Amount;
        public double Leverage;
        public double CounterLeverage;
        public double Duration;
        public PremiumData PremiumInfo;
        public double Apy;
        public bool BestApyForAmount;
    }

    public class CurrencyData
    {
        public Dictionary<string, Dictionary<string, Dictionary<string, Dictionary<string, PremiumData>>>> TakerHedge { get; set; }
    }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public class PremiumData
    {
        public double Total { get; set; }
        public double LiquidityPremium { get; set; }
        public double SettlementServiceFee { get; set; }
    }
}