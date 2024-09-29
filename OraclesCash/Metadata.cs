using System.Net.Http.Json;
using System.Text;

namespace OraclesCash;

public static partial class OraclesCash
{
    public static async Task<OracleMetadata> GetMetadata(string oraclePublicKey)
    {
        string url = $"https://oracles.generalprotocols.com/api/v1/oracleMetadata?publicKey={oraclePublicKey}";
        HttpResponseMessage response = await OraclesCash.client.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var oracleMetadata = await response.Content.ReadFromJsonAsync<OracleMetadataResponse>();

        if (oracleMetadata != null && oracleMetadata.OracleMetadata != null && oracleMetadata.OracleMetadata.Count == 15)
        {
            var metadata = new OracleMetadata();
            foreach (var message in oracleMetadata.OracleMetadata)
            {
                metadata.ParseMessage(message);
            }

            return metadata;
        }

        throw new ApplicationException($"Error fetching metadata: {oracleMetadata}");
    }

    class OracleMetadataResponse
    {
        public List<OracleMessage> OracleMetadata { get; set; }
    }

    public class OracleMetadata
    {
        // -1
        public string OPERATOR_NAME; // Human readable name of the operator of the oracle

        // -2
        public string OPERATOR_WEBSITE; // Link to more information on the operator of the oracle

        // -3
        public string RELAY_SERVER; // Link to relay server where this oracle's messages can be acquired

        // -4
        public long STARTING_TIMESTAMP; // Unix timestamp at which users can expect the oracle to start reliably producing price messages

        // -5
        public long ENDING_TIMESTAMP; // Unix timestamp at which users can expect the oracle to stop reliably producing price messages

        // -6
        public int ATTESTATION_SCALING; // Scaling value that the source price was multiplied by to retain precision when stored as an integer in oracle messages

        // -7
        public long ATTESTATION_PERIOD; // How often the oracle creates a price message in milliseconds

        // -8
        public string OPERATOR_HASH; // Sha256 hash of the policy referenced by OPERATOR_WEBSITE

        // -51
        public string SOURCE_NAME; // Human readable name of the data source the oracle observes

        // -52
        public string SOURCE_WEBSITE; // Link to more information on the data source the oracle

        // -53
        public string SOURCE_NUMERATOR_UNIT_NAME; // Human readable name of the original source data numerator

        // -54
        public string SOURCE_NUMERATOR_UNIT_CODE; // Short code of the original source data numerator (commonly ISO-4217 currency codes)

        // -55
        public string SOURCE_HASH; // Sha256 hash of the policy referenced by SOURCE_WEBSITE

        // -56
        public string SOURCE_DENOMINATOR_UNIT_NAME; // Human readable name of the original source data denominator

        // -57
        public string SOURCE_DENOMINATOR_UNIT_CODE; // Short code of the original source data denominator (commonly ISO-4217 currency codes)

        internal void ParseMessage(OracleMessage message)
        {
            var parsedMessage = ParseMessage(message.Message);

            switch (parsedMessage.NegativeFieldId)
            {
                case -1:
                    OPERATOR_NAME = parsedMessage.Content;
                    break;
                case -2:
                    OPERATOR_WEBSITE = parsedMessage.Content;
                    break;
                case -3:
                    RELAY_SERVER = parsedMessage.Content;
                    break;
                case -4:
                    STARTING_TIMESTAMP = long.Parse(parsedMessage.Content);
                    break;
                case -5:
                    ENDING_TIMESTAMP = long.Parse(parsedMessage.Content);
                    break;
                case -6:
                    ATTESTATION_SCALING = int.Parse(parsedMessage.Content);
                    break;
                case -7:
                    ATTESTATION_PERIOD = long.Parse(parsedMessage.Content);
                    break;
                case -8:
                    OPERATOR_HASH = parsedMessage.Content;
                    break;
                case -51:
                    SOURCE_NAME = parsedMessage.Content;
                    break;
                case -52:
                    SOURCE_WEBSITE = parsedMessage.Content;
                    break;
                case -53:
                    SOURCE_NUMERATOR_UNIT_NAME = parsedMessage.Content;
                    break;
                case -54:
                    SOURCE_NUMERATOR_UNIT_CODE = parsedMessage.Content;
                    break;
                case -55:
                    SOURCE_HASH = parsedMessage.Content;
                    break;
                case -56:
                    SOURCE_DENOMINATOR_UNIT_NAME = parsedMessage.Content;
                    break;
                case -57:
                    SOURCE_DENOMINATOR_UNIT_CODE = parsedMessage.Content;
                    break;
                default:
                    Console.WriteLine($"Unknown FieldId: {parsedMessage.FieldId}");
                    break;
            }
        }

        public class ParsedMessage
        {
            public DateTime Timestamp { get; set; }
            public uint FieldId { get; set; }
            public int NegativeFieldId { get; set; }
            public string Content { get; set; }
        }

        private static ParsedMessage ParseMessage(string hexString)
        {
            byte[] bytes = Convert.FromHexString(hexString);

            int index = 0;

            // Parse Timestamp (4 bytes, little-endian)
            uint timestamp = BitConverter.ToUInt32(bytes, index);
            index += 4;

            // Parse Field ID (4 bytes, little-endian)
            uint fieldId = BitConverter.ToUInt32(bytes, index);
            index += 4;

            // Parse Negative Field ID (4 bytes, little-endian, signed integer)
            int negativeFieldId = BitConverter.ToInt32(bytes, index);
            index += 4;

            // Remaining bytes are the message content
            string content = Encoding.UTF8.GetString(bytes, index, bytes.Length - index);

            // Convert timestamp to DateTime
            DateTime dateTime = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime;

            return new ParsedMessage
            {
                Timestamp = dateTime,
                FieldId = fieldId,
                NegativeFieldId = negativeFieldId,
                Content = content
            };
        }
    }
}