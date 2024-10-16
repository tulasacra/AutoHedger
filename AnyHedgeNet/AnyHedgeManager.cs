using System.Globalization;
using NBitcoin;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

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
        return contracts.ToList();
    }

    public async Task<Contract> GetContract(string contractAddress)
    {
        var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                WorkingDirectory = "JavaScript",
                FileName = "node",
                Arguments = $"status.mjs {authenticationToken} {accountPrivateKeyWIF} {ToCashAddr(contractAddress)}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        await process.WaitForExitAsync();
        string result = await process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync();

        if (process.ExitCode != 0)
        {
            throw new Exception(error);
        }
        
        try
        {
            return JsonConvert.DeserializeObject<Contract>(result);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }
    
    public async Task<Contract> CreateContract(string payoutAddress, string privateKeyWIF, decimal amountNominal, string oracleKey, double durationSeconds)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            WorkingDirectory = "JavaScript",
            FileName = "node",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        
        startInfo.ArgumentList.Add("liquidity-provider.mjs");
        startInfo.ArgumentList.Add(authenticationToken);
        startInfo.ArgumentList.Add(accountPrivateKeyWIF);
        startInfo.ArgumentList.Add(payoutAddress);
        startInfo.ArgumentList.Add(amountNominal.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(oracleKey);
        //startInfo.ArgumentList.Add(TimeSpan.FromDays(durationDays).TotalSeconds.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(durationSeconds.ToString(CultureInfo.InvariantCulture));
        
        var process = new System.Diagnostics.Process
        {
            StartInfo = startInfo
        };

        process.Start();
        await process.WaitForExitAsync();
        string result = await process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync();

        if (process.ExitCode != 0)
        {
            throw new Exception(error);
        }
        
        try
        {
            return JsonConvert.DeserializeObject<Contract>(result);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    private static Dictionary<string, string?> contractsCache = new();

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
                return tx["balance_change"].ToString().StartsWith('-') && !contractsCache.ContainsKey(txid);
            })
            .Select(tx=> tx["hash"].ToString());

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
                return !contractsCache.ContainsKey(txid);
            })
            .Select(tx=> tx["tx_hash"].ToString());

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
        string cashAddr = ToCashAddr(legacyAddress.ToString());

        //var txIds = await GetTxIds_blockchair(cashAddr);
        var txIds = await GetTxIds_fullstack(legacyAddress.ToString());

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
        var newContracts = (await Task.WhenAll(tasks)).SelectMany(x => x);
        var result = contractsCache.Values
            .Concat(newContracts.Select(x => x.contractId))
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => (string)id)
            .ToList();

        foreach (var newContract in newContracts)
        {
            contractsCache.Add(newContract.txid, newContract.contractId);
        }

        return result;
    }

    static string ToCashAddr(string legacyAddress)
    {
        const string prefix = "bitcoincash:";
        if (!legacyAddress.StartsWith(prefix))
        {
            return $"{prefix}{legacyAddress}";
        }
        else
        {
            return legacyAddress;
        }
    }
}