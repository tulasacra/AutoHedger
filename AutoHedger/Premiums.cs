using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoHedger
{
    public static class Premiums
    {
        private static readonly HttpClient client = new HttpClient();

        public static async Task<List<PremiumDataItem>> GetPremiums(string currency, string counterLeverage)
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
                return currencyData.TakerHedge
                    .SelectMany(amount => amount.Value
                        .SelectMany(leverage => leverage.Value
                            .Where(cl => cl.Key == counterLeverage)
                            .SelectMany(cl => cl.Value
                                .Select(duration => new PremiumDataItem
                                {
                                    Amount = amount.Key,
                                    Leverage = leverage.Key,
                                    CounterLeverage = cl.Key,
                                    Duration = duration.Key,
                                    PremiumInfo = duration.Value
                                }))))
                    .ToList();
            }

            return new List<PremiumDataItem>();
        }
    }

    public static class OracleKeys
    {
        public const string USD = "02d09db08af1ff4e8453919cc866a4be427d7bfe18f2c05e5444c196fcf6fd2818";
        public const string EUR = "02bb9b3324df889a66a57bc890b3452b84a2a74ba753f8842b06bba03e0fa0dfc5";
        public const string INR = "02e82ad82eb88fcdfd02fd5e2e0a67bc6ef4139bbcb63ce0b107a7604deb9f7ce1";
        public const string CNY = "030654b9598186fe4bc9e1b0490c6b85b13991cdb9a7afa34af1bbeee22a35487a";
        public const string XAU = "021f8338ccd45a7790025de198a266f252ac43c95bf81d2469feff110beeac89dd";
        public const string XAG = "02712c349ebb7555b17bdbbe9f7aad5a337fa4179d0680eec3f6c8d77bac9cfa79";
        public const string BTC = "0245a107de5c6aabc9e7b976f26625b01474f90d1a7d11c180bec990b6938e731e";
        public const string ETH = "038ab22e37cf020f6bbef40111ddc51083a936f0821de56ac01f799cf15b87904d";
    }

    public class PremiumDataItem
    {
        public string Amount { get; set; }
        public string Leverage { get; set; }
        public string CounterLeverage { get; set; }
        public string Duration { get; set; }
        public PremiumData PremiumInfo { get; set; }
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