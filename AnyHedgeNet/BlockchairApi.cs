using Newtonsoft.Json.Linq;

namespace AnyHedgeNet;

public static class BlockchairApi
{
    public static async Task<List<Transaction>> GetTransactions(IList<string> txIds)
    {
        var fromCache = txIds.Select(id => TxCache.Instance.TryGetValue(id, out var tx) ? tx : null)
            .Where(tx => tx != null)
            .ToList();
        
        if (fromCache.Count == txIds.Count)
            return fromCache!;
            
        var missingTxIds = txIds.Where(id => !TxCache.Instance.ContainsKey(id)).ToList();
        
        using HttpClient client = new HttpClient();
        var chunks = missingTxIds.Chunk(10).ToArray();
        var tasks = new Task<List<Transaction>>[chunks.Length];
        
        for (int i = 0; i < chunks.Length; i++)
        {
            var chunk = chunks[i];
            tasks[i] = GetTransactionsChunk(client, chunk);
        }

        foreach (var task in tasks)
        {
            await task;
            foreach (var result in task.Result)
            {
                TxCache.Instance.Add(result.TxId, result);
            }
            TxCache.Instance.Save();
            if (chunks.Count() > 3)
            {
                await Task.Delay(TimeSpan.FromMinutes(2));
            }
        }
        
        //var results = await Task.WhenAll(tasks);
        //return results.SelectMany(x => x).ToList();
        return tasks.SelectMany(x => x.Result).Concat(fromCache).ToList()!; 
    }
    
    private static async Task<List<Transaction>> GetTransactionsChunk(HttpClient client, string[] txIdsChunk)
    {
        var transactions = new List<Transaction>();
        
        string txUrl = $"https://api.blockchair.com/bitcoin-cash/dashboards/transactions/{string.Join(',', txIdsChunk)}";
        HttpResponseMessage txResponse = await client.GetAsync(txUrl);

        if (!txResponse.IsSuccessStatusCode)
        {
            throw new Exception($"Error fetching transactions: {txResponse.ReasonPhrase}");
        }

        string txJsonResult = await txResponse.Content.ReadAsStringAsync();
        JObject txJson = JObject.Parse(txJsonResult);

        foreach (JProperty tx in txJson["data"])
        {
            var txid = tx.Name;
            var txData = tx.Value;
            var outputsJson = txData["outputs"];
            var inputsJson = txData["inputs"];

            string? contractAddress = null;
            foreach (var output in outputsJson)
            {
                string type = output["type"].ToString();

                if (type == "scripthash")
                {
                    contractAddress = output["recipient"].ToString();
                    break;
                }
            }

            var inputs = new List<TransactionInput>();
            foreach (var input in inputsJson)
            {
                inputs.Add(new TransactionInput
                {
                    BlockId = input["block_id"]?.ToObject<long?>(),
                    TransactionId = input["transaction_id"]?.ToObject<long?>(),
                    Index = input["index"]?.ToObject<int?>(),
                    TransactionHash = input["transaction_hash"]?.ToString(),
                    Date = input["date"]?.ToString(),
                    Time = input["time"]?.ToString(),
                    Value = input["value"]?.ToObject<long?>(),
                    ValueUsd = input["value_usd"]?.ToObject<double?>(),
                    Recipient = input["recipient"]?.ToString(),
                    Type = input["type"]?.ToString(),
                    ScriptHex = input["script_hex"]?.ToString(),
                    IsFromCoinbase = input["is_from_coinbase"]?.ToObject<bool?>(),
                    IsSpendable = input["is_spendable"]?.ToObject<bool?>(),
                    IsSpent = input["is_spent"]?.ToObject<bool?>(),
                    SpendingBlockId = input["spending_block_id"]?.ToObject<long?>(),
                    SpendingTransactionId = input["spending_transaction_id"]?.ToObject<long?>(),
                    SpendingIndex = input["spending_index"]?.ToObject<int?>(),
                    SpendingTransactionHash = input["spending_transaction_hash"]?.ToString(),
                    SpendingDate = input["spending_date"]?.ToString(),
                    SpendingTime = input["spending_time"]?.ToString(),
                    SpendingValueUsd = input["spending_value_usd"]?.ToObject<double?>(),
                    SpendingSequence = input["spending_sequence"]?.ToObject<long?>(),
                    SpendingSignatureHex = input["spending_signature_hex"]?.ToString(),
                    Lifespan = input["lifespan"]?.ToObject<long?>(),
                    Cdd = input["cdd"]?.ToObject<double?>()
                });
            }

            var outputs = new List<TransactionOutput>();
            foreach (var output in outputsJson)
            {
                outputs.Add(new TransactionOutput
                {
                    BlockId = output["block_id"]?.ToObject<long?>(),
                    TransactionId = output["transaction_id"]?.ToObject<long?>(),
                    Index = output["index"]?.ToObject<int?>(),
                    TransactionHash = output["transaction_hash"]?.ToString(),
                    Date = output["date"]?.ToString(),
                    Time = output["time"]?.ToString(),
                    Value = output["value"]?.ToObject<long?>(),
                    ValueUsd = output["value_usd"]?.ToObject<double?>(),
                    Recipient = output["recipient"]?.ToString(),
                    Type = output["type"]?.ToString(),
                    ScriptHex = output["script_hex"]?.ToString(),
                    IsFromCoinbase = output["is_from_coinbase"]?.ToObject<bool?>(),
                    IsSpendable = output["is_spendable"]?.ToObject<bool?>(),
                    IsSpent = output["is_spent"]?.ToObject<bool?>(),
                    SpendingBlockId = output["spending_block_id"]?.ToObject<long?>(),
                    SpendingTransactionId = output["spending_transaction_id"]?.ToObject<long?>(),
                    SpendingIndex = output["spending_index"]?.ToObject<int?>(),
                    SpendingTransactionHash = output["spending_transaction_hash"]?.ToString(),
                    SpendingDate = output["spending_date"]?.ToString(),
                    SpendingTime = output["spending_time"]?.ToString(),
                    SpendingValueUsd = output["spending_value_usd"]?.ToObject<double?>(),
                    SpendingSequence = output["spending_sequence"]?.ToObject<long?>(),
                    SpendingSignatureHex = output["spending_signature_hex"]?.ToString(),
                    Lifespan = output["lifespan"]?.ToObject<long?>(),
                    Cdd = output["cdd"]?.ToObject<double?>()
                });
            }

            var transaction = new Transaction
            {
                TxId = txid,
                ContractAddress = contractAddress,
                RawData = txData,
                Inputs = inputs,
                Outputs = outputs
            };

            if (transaction.Inputs == null || transaction.Inputs.Count == 0 ||
                transaction.Outputs == null || transaction.Outputs.Count == 0)
            {
                throw new Exception($"Transaction {txid} returned with empty inputs or outputs");
            }

            transactions.Add(transaction);
        }

        return transactions;
    }
    
    public class Transaction
    {
        public string TxId { get; set; }
        public string? ContractAddress { get; set; }
        public JToken RawData { get; set; }
        public List<TransactionInput> Inputs { get; set; }
        public List<TransactionOutput> Outputs { get; set; }
    }

    public class TransactionInput
    {
        public long? BlockId { get; set; }
        public long? TransactionId { get; set; }
        public int? Index { get; set; }
        public string TransactionHash { get; set; }
        public string Date { get; set; }
        public string Time { get; set; }
        public long? Value { get; set; }
        public double? ValueUsd { get; set; }
        public string Recipient { get; set; }
        public string Type { get; set; }
        public string ScriptHex { get; set; }
        public bool? IsFromCoinbase { get; set; }
        public bool? IsSpendable { get; set; }
        public bool? IsSpent { get; set; }
        public long? SpendingBlockId { get; set; }
        public long? SpendingTransactionId { get; set; }
        public int? SpendingIndex { get; set; }
        public string SpendingTransactionHash { get; set; }
        public string SpendingDate { get; set; }
        public string SpendingTime { get; set; }
        public double? SpendingValueUsd { get; set; }
        public long? SpendingSequence { get; set; }
        public string SpendingSignatureHex { get; set; }
        public long? Lifespan { get; set; }
        public double? Cdd { get; set; }
    }

    public class TransactionOutput
    {
        public long? BlockId { get; set; }
        public long? TransactionId { get; set; }
        public int? Index { get; set; }
        public string TransactionHash { get; set; }
        public string Date { get; set; }
        public string Time { get; set; }
        public long? Value { get; set; }
        public double? ValueUsd { get; set; }
        public string Recipient { get; set; }
        public string Type { get; set; }
        public string ScriptHex { get; set; }
        public bool? IsFromCoinbase { get; set; }
        public bool? IsSpendable { get; set; }
        public bool? IsSpent { get; set; }
        public long? SpendingBlockId { get; set; }
        public long? SpendingTransactionId { get; set; }
        public int? SpendingIndex { get; set; }
        public string SpendingTransactionHash { get; set; }
        public string SpendingDate { get; set; }
        public string SpendingTime { get; set; }
        public double? SpendingValueUsd { get; set; }
        public long? SpendingSequence { get; set; }
        public string SpendingSignatureHex { get; set; }
        public long? Lifespan { get; set; }
        public double? Cdd { get; set; }
    }
}