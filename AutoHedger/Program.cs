using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Timers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Timer = System.Timers.Timer;

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

class Program
{
    private const string currency = OracleKeys.BTC;
    private const double minimumApy = 5;

    
    private static readonly HttpClient client = new HttpClient();
    private static Timer timer;

    static async Task Main(string[] args)
    {
        timer = new Timer(900000); // 15 minutes in milliseconds
        timer.Elapsed += async (sender, e) => await CheckPremiums();
        timer.AutoReset = true;
        timer.Enabled = true;

        // Run CheckPremiums immediately after starting
        await CheckPremiums();
        Console.ReadLine();
    }


    private static async Task CheckPremiums()
    {
        Console.Clear();
        Console.WriteLine($"Checking premiums at: {DateTime.Now}");
        Console.WriteLine("----------------------------------------");

        try
        {
            string url = "https://premiums.anyhedge.com/api/v2/currentPremiumsV2";
            HttpResponseMessage response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            string responseBody = await response.Content.ReadAsStringAsync();

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var premiumData = JsonSerializer.Deserialize<Dictionary<string, CurrencyData>>(responseBody, options);

            if (premiumData.TryGetValue(currency, out var usdData))
            {
                Console.WriteLine("| Amount (BCH) | Duration (days) | Premium (%) | APY (%) | Status |");
                Console.WriteLine("|--------------|-----------------|-------------|---------|--------|");

                foreach (var (userAmount, leverageData) in usdData.TakerHedge)
                {
                    foreach (var (leverage, counterLeverageData) in leverageData)
                    {
                        foreach (var (counterLeverage, durationData) in counterLeverageData)
                        {
                            //only check 20% hedge
                            if (counterLeverage != "5") continue;
                            
                            foreach (var (duration, premiumInfo) in durationData)
                            {
                                if (premiumInfo.Total >= 0) continue;

                                double durationInDays = int.Parse(duration) / 86400.0; // Convert seconds to days
                                double yield = premiumInfo.Total / -100;
                                double apy = (Math.Pow(1 + yield, 365 / durationInDays) - 1) * 100; // Calculate APY
                                string status = apy >= minimumApy ? "OK" : "";
                                Console.WriteLine($"| {userAmount,-12} | {durationInDays,-15:F2} | {premiumInfo.Total,-11:F2} | {apy,-7:F2} | {status,-6} |");
                            }
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error: {e.Message}");
        }

        Console.WriteLine("----------------------------------------");
        Console.WriteLine("Press [Enter] to exit the program.");
    }
}

public class CurrencyData
{
    [JsonPropertyName("takerHedge")]
    public Dictionary<string, Dictionary<string, Dictionary<string, Dictionary<string, PremiumData>>>> TakerHedge { get; set; }
}

[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
public class PremiumData
{
    [JsonPropertyName("total")]
    public double Total { get; set; }

    [JsonPropertyName("liquidityPremium")]
    public double LiquidityPremium { get; set; }

    [JsonPropertyName("settlementServiceFee")]
    public double SettlementServiceFee { get; set; }
}