using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Timers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Timer = System.Timers.Timer;

class Program
{
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

            if (premiumData.TryGetValue("02d09db08af1ff4e8453919cc866a4be427d7bfe18f2c05e5444c196fcf6fd2818", out var usdData))
            {
                Console.WriteLine("| Amount (BCH) | Duration (days) | USD Premium (%) | APY (%) | Status |");
                Console.WriteLine("|--------------|-----------------|-----------------|---------|--------|");

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
                                string status = apy >= 5 ? "OK" : "";
                                Console.WriteLine($"| {userAmount,-12} | {durationInDays,-15:F2} | {premiumInfo.Total,-15:F2} | {apy,-7:F2} | {status,-6} |");
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