using System.Globalization;
using System.Text;
using NBitcoin;
using Newtonsoft.Json.Linq;

namespace AnyHedgeNet;

public class AnyHedgeManager
{
    private readonly string authenticationToken = "3c9a4fe90550861ebdeba1c30dc909c21e7d02c4438632a680b342422ad3b5f3";
    private readonly string accountPrivateKeyWIF;

    public AnyHedgeManager(string accountPrivateKeyWif, string? authenticationToken = null)
    {
        this.authenticationToken = authenticationToken ?? this.authenticationToken;
        this.accountPrivateKeyWIF = accountPrivateKeyWif;
    }

    public async Task<List<Contract>> GetContracts(List<string> contractAddress)
    {
        var contractTasks = contractAddress.Select(address => GetContract(address));
        var contracts = await Task.WhenAll(contractTasks);
        ContractCache.Instance.Save();
        return contracts.ToList();
    }

    private async Task<Contract> GetContract(string contractAddress)
    {
        if (ContractCache.Instance.Dictionary.ContainsKey(contractAddress))
        {
            return ContractCache.Instance.Dictionary[contractAddress];
        }
        
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                WorkingDirectory = "JavaScript",
                FileName = "node",
                Arguments = $"status.mjs {authenticationToken} {accountPrivateKeyWIF} {AddBchPrefix(contractAddress)}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch (TimeoutException)
        {
            process.Kill();
            throw new TimeoutException("Process execution timed out.");
        }

        string result = await process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync();

        if (process.ExitCode != 0)
        {
            throw new Exception($"{error}{Environment.NewLine}{result}");
        }
        
        var deserializeObject = JsonConvert.DeserializeObject<Contract>(result);
        ContractCache.Instance.Dictionary.AddOrReplace(contractAddress, deserializeObject);
        //ContractCache.Instance.Save();
        return deserializeObject;
    }

    public async Task<Contract> ProposeContract(string payoutAddress, string privateKeyWIF, decimal amountNominal, string oracleKey, double durationSeconds)
    {
        durationSeconds -= 120; //to prevent client/server time diff errors (expected contract duration to be in the range [7200, 7776000] but got 7776052)

        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = "JavaScript",
            FileName = "node",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("liquidity-provider.mjs");
        startInfo.ArgumentList.Add(authenticationToken);
        startInfo.ArgumentList.Add(payoutAddress);
        startInfo.ArgumentList.Add(amountNominal.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(oracleKey);
        startInfo.ArgumentList.Add(durationSeconds.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("NULL");
        startInfo.ArgumentList.Add("propose");


        var process = new Process();
        process.StartInfo = startInfo;
        StringBuilder resultBuilder = new();
        process.OutputDataReceived += (sender, args) => { resultBuilder.AppendLine(args.Data); };
        process.Start();
        process.BeginOutputReadLine();

        await process.StandardInput.WriteLineAsync(accountPrivateKeyWIF);
        process.StandardInput.Close();

        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch (TimeoutException)
        {
            process.Kill();
            throw new TimeoutException("Process execution timed out.");
        }

        var result = resultBuilder.ToString();
        var error = await process.StandardError.ReadToEndAsync();

        if (process.ExitCode != 0)
        {
            throw new Exception($"{error}{Environment.NewLine}{result}");
        }

        // useful for debug
        // StringBuilder sb = new();
        // var jsonObjects = result.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        // foreach (string json in jsonObjects)
        // {
        //     sb.AppendLine(JsonConvert.SerializeObject(JsonConvert.DeserializeObject(json), Formatting.Indented));
        // }
        //return sb.ToString();

        return JsonConvert.DeserializeObject<Contract>(result);
    }

    public async Task<string> FundContract(string payoutAddress, string privateKeyWIF, decimal amountNominal, string oracleKey, double durationSeconds, Contract contract)
    {
        durationSeconds -= 120; //to prevent client/server time diff errors (expected contract duration to be in the range [7200, 7776000] but got 7776052)

        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = "JavaScript",
            FileName = "node",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("liquidity-provider.mjs");
        startInfo.ArgumentList.Add(authenticationToken);
        startInfo.ArgumentList.Add(payoutAddress);
        startInfo.ArgumentList.Add(amountNominal.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(oracleKey);
        startInfo.ArgumentList.Add(durationSeconds.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(JsonConvert.SerializeObject(contract, new JsonSerializerSettings 
        {
            ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver(),
            Converters = new[] { new BigIntegerJsonConverter() }
        }));
        startInfo.ArgumentList.Add("fund");

        var process = new Process();
        process.StartInfo = startInfo;
        StringBuilder resultBuilder = new();
        process.OutputDataReceived += (sender, args) => { resultBuilder.AppendLine(args.Data); };
        process.Start();
        process.BeginOutputReadLine();

        await process.StandardInput.WriteLineAsync($"{accountPrivateKeyWIF},{privateKeyWIF}");
        process.StandardInput.Close();

        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch (TimeoutException)
        {
            process.Kill();
            throw new TimeoutException("Process execution timed out.");
        }

        var result = resultBuilder.ToString();
        var error = await process.StandardError.ReadToEndAsync();

        if (process.ExitCode != 0)
        {
            throw new Exception($"{error}{Environment.NewLine}{result}");
        }

        // useful for debug
        StringBuilder sb = new();
        var jsonObjects = result.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string json in jsonObjects)
        {
            sb.AppendLine(JsonConvert.SerializeObject(JsonConvert.DeserializeObject(json), Formatting.Indented));
        }

        return sb.ToString();

        //return JsonConvert.DeserializeObject<Contract>(result);
    }

    private async Task<IEnumerable<string>> GetTxIds_blockchair(string cashAddr)
    {
        using HttpClient client = new HttpClient();
        string url = $"https://api.blockchair.com/bitcoin-cash/dashboards/address/{cashAddr}?transaction_details=true";
        HttpResponseMessage response = await client.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Error fetching data: {response.ReasonPhrase}. URL: {url}");
        }

        string jsonResult = await response.Content.ReadAsStringAsync();
        JObject json = JObject.Parse(jsonResult);

        var data = json["data"][cashAddr];
        var txIds = data["transactions"]
            .Where(delegate(JToken tx)
            {
                string txid = tx["hash"].ToString();
                // only outgoing transactions are contract funding candidates
                return tx["balance_change"].ToString().StartsWith('-') && !ContractAddressCache.Instance.Dictionary.ContainsKey(txid);
            })
            .Select(tx => tx["hash"].ToString());

        return txIds;
    }

    private async Task<IEnumerable<string>> GetTxIds_fullstack(string legacyAddress)
    {
        using HttpClient client = new HttpClient();
        string url = $"https://api.fullstack.cash/v5/electrumx/transactions/{legacyAddress}";
        HttpResponseMessage response = await client.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Error fetching data: {response.ReasonPhrase}. URL: {url}");
        }

        string jsonResult = await response.Content.ReadAsStringAsync();
        JObject json = JObject.Parse(jsonResult);

        var txIds = json["transactions"]
            .Where(delegate(JToken tx)
            {
                string txid = tx["tx_hash"].ToString();
                return !ContractAddressCache.Instance.Dictionary.ContainsKey(txid);
            })
            .Select(tx => tx["tx_hash"].ToString());

        return txIds;
    }

    public async Task<List<string>> GetContractAddresses()
    {
        if (string.IsNullOrEmpty(accountPrivateKeyWIF))
        {
            return new List<string>(0);
        }

        Network network = Network.Main;
        BitcoinSecret secret = new BitcoinSecret(accountPrivateKeyWIF, network);
        PubKey pubKey = secret.PubKey;
        BitcoinAddress legacyAddress = pubKey.GetAddress(ScriptPubKeyType.Legacy, network);
        string cashAddr = AddBchPrefix(legacyAddress.ToString());

        IEnumerable<string> txIds;
        try
        {
            txIds = await GetTxIds_fullstack(legacyAddress.ToString());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in GetTxIds_fullstack: {ex.Message}");
            txIds = await GetTxIds_blockchair(cashAddr);
        }

        List<Task<List<(string txid, string? contractId)>>> tasks = new();
        using HttpClient client = new HttpClient();

        foreach (var txids in txIds.Chunk(10))
        {
            tasks.Add(Task.Run(async () =>
            {
                string txUrl = $"https://api.blockchair.com/bitcoin-cash/dashboards/transactions/{string.Join(',', txids)}";
                HttpResponseMessage txResponse = await client.GetAsync(txUrl);

                if (!txResponse.IsSuccessStatusCode)
                {
                    throw new Exception($"Error fetching transactions: {txResponse.ReasonPhrase}");
                }

                string txJsonResult = await txResponse.Content.ReadAsStringAsync();
                JObject txJson = JObject.Parse(txJsonResult);

                var results = new List<(string txId, string? contractId)>();
                foreach (JProperty tx in txJson["data"])
                {
                    //var txid = tx["transaction"]["hash"].ToString();
                    var txid = tx.Name;
                    var outputs = tx.Value["outputs"];

                    string? contractId = null;
                    foreach (var output in outputs)
                    {
                        string type = output["type"].ToString();

                        if (type == "scripthash")
                        {
                            contractId = output["recipient"].ToString();
                            break;
                        }
                    }

                    results.Add((txid, contractId));
                }

                return results;
            }));
        }

        // add newContracts to those from cache and filter out null/empty (txids that are not contract fundings) 
        var newContracts = (await Task.WhenAll(tasks)).SelectMany(x => x).ToArray();
        List<string> result = ContractAddressCache.Instance.Dictionary.Values
            .Concat(newContracts.Select(x => x.contractId))
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => (string)id)
            .ToList();

        foreach (var newContract in newContracts)
        {
            ContractAddressCache.Instance.Dictionary.AddOrReplace(newContract.txid, newContract.contractId);
        }

        if (newContracts.Any())
        {
            ContractAddressCache.Instance.Save();
        }

        return result;
    }

    static string AddBchPrefix(string address)
    {
        const string prefix = "bitcoincash:";
        if (!address.StartsWith(prefix))
        {
            return $"{prefix}{address}";
        }
        else
        {
            return address;
        }
    }
}

public class BigIntegerJsonConverter : JsonConverter<BigInteger>
{
    public override BigInteger ReadJson(JsonReader reader, Type objectType, BigInteger existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        //none of this works. JsonReaderException happens before the code gets here.
        
        //return BigInteger.Parse(((string)reader.Value).TrimEnd('n'));
        var token = JToken.Load(reader);
        return BigInteger.Parse(token.ToString().TrimEnd('n'));
    }

    public override void WriteJson(JsonWriter writer, BigInteger value, JsonSerializer serializer)
    {
        writer.WriteValue(value.ToString()+'n');
    }
}