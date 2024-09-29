using System.Net.Http.Json;

namespace OraclesCash;

public static partial class OraclesCash
{
    private static readonly HttpClient client = new HttpClient();

    public static async Task<decimal?> GetLatestPrice(string oraclePublicKey)
    {
        //try
        {
            string url = $"https://oracles.generalprotocols.com/api/v1/oracleMessages?publicKey={oraclePublicKey}";
            HttpResponseMessage response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var priceData = await response.Content.ReadFromJsonAsync<OracleMessagesResponse>();

            if (priceData != null && priceData.OracleMessages != null && priceData.OracleMessages.Any())
            {
                // Assuming the latest price is in the first message
                var latestMessage = priceData.OracleMessages.First();

                //int? scaling = await OraclesCash.GetAttestationScaling(oraclePublicKey);
                OracleMetadata metadata = await GetMetadata(oraclePublicKey);

                return ParsePriceFromMessage(latestMessage.Message, metadata.ATTESTATION_SCALING);
            }

            return null;
        }
        // catch (Exception e)
        // {
        //     Console.WriteLine($"Error fetching latest price: {e.Message}");
        //     return null;
        // }
    }

    private static decimal? ParsePriceFromMessage(string message, int scaling)
    {
        try
        {
            byte[] messageBytes = Convert.FromHexString(message);

            if (messageBytes.Length != 16)
            {
                throw new Exception("Message must be exactly 16 bytes long.");
            }

            int messageTimestamp = BitConverter.ToInt32(messageBytes, 0);
            int messageSequence = BitConverter.ToInt32(messageBytes, 4);
            int contentSequence = BitConverter.ToInt32(messageBytes, 8);
            int price = BitConverter.ToInt32(messageBytes, 12);

            return (decimal)price / scaling;
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error parsing price from message: {e.Message}");
            return null;
        }
    }
}

class OracleMessagesResponse
{
    public List<OracleMessage> OracleMessages { get; set; }
}

class OracleMessage
{
    public string Message { get; set; }
    public string PublicKey { get; set; }
    public string Signature { get; set; }
}