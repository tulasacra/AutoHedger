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
                Arguments = $"status.mjs {authenticationToken} {ToCashAddr(contractAddress)} {accountPrivateKeyWIF}",
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
    
    public async Task<Contract> CreateContract(string privateKeyWIF)
    {
        var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                WorkingDirectory = "JavaScript",
                FileName = "node",
                Arguments = $"liquidity-provider.mjs {authenticationToken} {privateKeyWIF}",
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

    private static Dictionary<string, string?> contractsCache = new(); 

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

        using (HttpClient client = new HttpClient())
        {
            string url = $"https://api.blockchair.com/bitcoin-cash/dashboards/address/{cashAddr}?transaction_details=true";

            HttpResponseMessage response = await client.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                string jsonResult = await response.Content.ReadAsStringAsync();
                JObject json = JObject.Parse(jsonResult);

                var data = json["data"][cashAddr];
                // only outgoing transactions are contract funding candidates
                var transactions = data["transactions"].Where(x=> x["balance_change"].ToString().StartsWith('-'));

                List<Task<(string txid, string? contractId)>> tasks = new ();
                
                foreach (var tx in transactions)
                {
                    string txid = tx["hash"].ToString();

                    if (!contractsCache.ContainsKey(txid))
                    {
                        tasks.Add(Task.Run(async () =>
                        {
                            string txUrl = $"https://api.blockchair.com/bitcoin-cash/dashboards/transaction/{txid}";
                            HttpResponseMessage txResponse = await client.GetAsync(txUrl);

                            if (txResponse.IsSuccessStatusCode)
                            {
                                string txJsonResult = await txResponse.Content.ReadAsStringAsync();
                                JObject txJson = JObject.Parse(txJsonResult);

                                var outputs = txJson["data"][txid]["outputs"];

                                foreach (var output in outputs)
                                {
                                    string type = output["type"].ToString();

                                    if (type == "scripthash")
                                    {
                                        var contractId = output["recipient"].ToString();
                                        return (txid, contractId);
                                    }
                                }

                                return (txid, null);
                            }
                            else
                            {
                                throw new Exception($"Error fetching transaction {txid}: {txResponse.ReasonPhrase}");
                            }
                        }));
                    }
                }

                // add newContracts to those from cache and filter out null/empty (txids that are not contract fundings) 
                var newContracts = await Task.WhenAll(tasks);
                var result = contractsCache.Values
                    .Concat(newContracts.Select(x=>x.contractId))
                    .Where(id => !string.IsNullOrEmpty(id))
                    .Select(id => (string)id)
                    .ToList();

                foreach (var newContract in newContracts)
                {
                    contractsCache.Add(newContract.txid, newContract.contractId);
                }
                
                return result;
            }
            else
            {
                throw new Exception($"Error fetching data: {response.ReasonPhrase}. URL: {url}");
            }
        }
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