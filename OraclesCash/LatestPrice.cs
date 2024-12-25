using System.Net.Http.Json;

namespace OraclesCash;

public static partial class OraclesCashService
{
    private static readonly HttpClient client = new HttpClient();

    public static async Task<decimal> GetLatestPrice(string oraclePublicKey, OracleMetadata? metadata = null)
    {
        string url = $"https://oracles.generalprotocols.com/api/v1/oracleMessages?publicKey={oraclePublicKey}";
        HttpResponseMessage response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var priceData = await response.Content.ReadFromJsonAsync<OracleMessagesResponse>();

        if (priceData == null || priceData.OracleMessages == null || !priceData.OracleMessages.Any())
            throw new ApplicationException($"Error fetching latest price {priceData}");
        
        // Assuming the latest price is in the first message
        var latestMessage = priceData.OracleMessages.First();

        if (metadata == null)
        {
            metadata = await GetMetadata(oraclePublicKey);
        }

        return ParsePriceMessage(latestMessage.Message, metadata.ATTESTATION_SCALING).price;
    }

    public static ParsedPriceMessage ParsePriceMessage(string message, int scaling)
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

        return new ParsedPriceMessage
        {
            messageTimestamp = messageTimestamp,
            messageSequence = messageSequence,
            contentSequence = contentSequence,
            price = (decimal)price / scaling,

        };
    }

    public static async Task<decimal[]> GetLatestPrice((string OracleKey, OracleMetadata OracleMetadata)[] o)
    {
        var tasks = o.Select(oracle => GetLatestPrice(oracle.OracleKey, oracle.OracleMetadata));
        return await Task.WhenAll(tasks);
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

public class ParsedPriceMessage
{
    public int messageTimestamp;
    public int messageSequence;
    public int contentSequence;
    public decimal price;
}