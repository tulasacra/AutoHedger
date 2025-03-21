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
        var fromCache = contractAddress.Select(id => ContractCache.Instance.TryGetValue(id, out var tx) ? tx : null)
            .Where(tx => tx != null)
            .ToList();
        if (fromCache.Count == contractAddress.Count)
            return fromCache!;
        
        var contractTasks = contractAddress.Select(address => GetContract(address));
        var contracts = await Task.WhenAll(contractTasks);
        ContractCache.Instance.Save();
        return contracts.ToList();
    }

    private async Task<Contract> GetContract(string contractAddress)
    {
        if (ContractCache.Instance.TryGetValue(contractAddress, out var cachedContract))
        {
            return cachedContract;
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
        if (deserializeObject.IsSettled)
        {
            ContractCache.Instance.Add(contractAddress, deserializeObject);
            //ContractCache.Instance.Save();
        }
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

    public async Task<List<TxMetadata>> GetTransactions()
    {
        if (string.IsNullOrEmpty(accountPrivateKeyWIF))
        {
            return new List<TxMetadata>(0);
        }

        Network network = Network.Main;
        BitcoinSecret secret = new BitcoinSecret(accountPrivateKeyWIF, network);
        PubKey pubKey = secret.PubKey;
        BitcoinAddress legacyAddress = pubKey.GetAddress(ScriptPubKeyType.Legacy, network);
        string cashAddr = AddBchPrefix(legacyAddress.ToString());

        var txIds = await ElectrumNetworkProvider.GetTxIds(legacyAddress.ToString());

        var newTransactions = (await BlockchairApi.GetTransactions(txIds
                .Where(x => !TxMetadataCache.Instance.ContainsKey(x))
                .ToList()))
            .Select(x => new TxMetadata()
            {
                TxId = x.TxId,
                PreFundingTxId = x.ContractAddress == null ? null : x.Inputs.First().TransactionHash,
                ContractAddress = AddBchPrefix(x.ContractAddress)
            })
            .ToList();

        var prefundingTxs = await BlockchairApi.GetTransactions(newTransactions
            .Where(x=>x.PreFundingTxId != null)
            .Select(x => x.PreFundingTxId!)
            .ToList());
        foreach (var tx in newTransactions.Where(x=>x.PreFundingTxId != null))
        {
            tx.PreFundingAddress = AddBchPrefix(prefundingTxs.First(x=>tx.PreFundingTxId! == x.TxId).Inputs.First().Recipient);
        }

        // add newTransactions to those from cache and filter out null/empty (transactions that are not contract fundings) 
        var result = TxMetadataCache.Instance.Values
            .Concat(newTransactions)
            .Where(tx => !string.IsNullOrEmpty(tx.ContractAddress))
            .ToList();

        foreach (var newContract in newTransactions)
        {
            TxMetadataCache.Instance.Add(newContract.TxId, newContract);
        }
        if (newTransactions.Any())
        {
            TxMetadataCache.Instance.Save();
        }

        return result;
    }

    public class TxMetadata
    {
        public string TxId;
        public string? PreFundingTxId;
        private string? _contractAddress;
        private string? _preFundingAddress;

        public string? PreFundingAddress
        {
            get => _preFundingAddress;
            set => _preFundingAddress = AddBchPrefix(value);
        }

        public string? ContractAddress
        {
            get => _contractAddress;
            set => _contractAddress = AddBchPrefix(value);
        }
    }

    static string? AddBchPrefix(string? address)
    {
        if (string.IsNullOrEmpty(address)) return null;
        
        const string prefix = "bitcoincash:";
        if (address.StartsWith(prefix))
        {
            return address;
        }

        return $"{prefix}{address}";
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