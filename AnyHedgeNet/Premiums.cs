using System.Text.Json;
using System.Text.Json.Serialization;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace AnyHedgeNet
{
    public static class Premiums
    {
        private static readonly HttpClient client = new HttpClient();

        public static async Task<List<PremiumDataItem>> GetPremiums(string counterLeverage, decimal maximumPremium, string? currencyOracleKey = null)
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

            var result = new List<PremiumDataItem>(allData.Count * 7 * 3);

            foreach (var currencyData in allData)
            {
                if (currencyOracleKey == null || currencyData.Key == currencyOracleKey)
                {
                    var premiumDataItems = currencyData.Value.Fees.TakerHedge
                        .SelectMany(amount => amount.Value
                            .SelectMany(leverage => leverage.Value
                                .Where(cl => cl.Key == counterLeverage)
                                .SelectMany(cl => cl.Value
                                    .Where(x => x.Value.Total <= maximumPremium)
                                    .Select(duration =>
                                    {
                                        double durationInSeconds = double.Parse(duration.Key);
                                        double durationInDays = durationInSeconds / 86400.0;
                                        decimal yieldInPercent = duration.Value.Total / -1;

                                        return new PremiumDataItem
                                        {
                                            Amount = decimal.Parse(amount.Key),
                                            Leverage = double.Parse(leverage.Key),
                                            CounterLeverage = double.Parse(cl.Key),
                                            DurationSeconds = durationInSeconds,
                                            DurationDays = durationInDays,
                                            PremiumInfo = duration.Value,
                                            Yield = yieldInPercent,
                                            Apy = YieldToApy(yieldInPercent, durationInDays),
                                        };
                                    }))))
                        .ToList();

                    var bestApyForAmount = premiumDataItems
                        .GroupBy(item => item.Amount)
                        .Select(group => group.OrderByDescending(item => item.Apy).First())
                        .ToDictionary(item => item.Amount, item => item.Apy);

                    foreach (var item in premiumDataItems)
                    {
                        item.CurrencyOracleKey = currencyData.Key;
                        item.BestApyForAmount = item.Apy == bestApyForAmount[item.Amount];
                    }

                    result.AddRange(premiumDataItems);
                }
            }

            return result;
        }
        
        public static decimal YieldToApy(decimal yieldInPercent, double durationInDays)
        {
            var yieldInFractions = yieldInPercent / 100;
            double result = (Math.Pow((double)(1 + yieldInFractions), 365 / durationInDays) - 1) * 100;
            if (result > (double)decimal.MaxValue)
                return decimal.MaxValue;
            if (result < (double)decimal.MinValue)
                return decimal.MinValue;
            return (decimal)result;
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
        public double DurationSeconds;
        public double DurationDays;
        public PremiumData PremiumInfo;
        public decimal Yield;
        public decimal Apy;
        
        public string CurrencyOracleKey;
        public bool BestApyForAmount;

        public PremiumDataItem()
        {
        }

        public PremiumDataItem(decimal amount, double durationDays, decimal yield)
        {
            Amount = amount;
            DurationDays = durationDays;
            Yield = yield;
            Apy = Premiums.YieldToApy(yield, durationDays);
        }
    }

    public class CurrencyData
    {
        public long Timestamp { get; set; }
        public Fees Fees { get; set; }
    }

    public class Fees
    {
        public Dictionary<string, Dictionary<string, Dictionary<string, Dictionary<string, PremiumData>>>> TakerHedge { get; set; }
    }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public class PremiumData
    {
        public decimal Total { get; set; }
        public decimal LiquidityPremium { get; set; }
        public decimal SettlementServiceFee { get; set; }
    }
}