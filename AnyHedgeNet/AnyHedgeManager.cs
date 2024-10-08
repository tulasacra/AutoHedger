using NBitcoin;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace AnyHedgeNet;

public class AnyHedgeManager
{
    private readonly string authenticationToken = "3c9a4fe90550861ebdeba1c30dc909c21e7d02c4438632a680b342422ad3b5f3";
    private readonly string privateKeyWIF;

    public AnyHedgeManager(string privateKeyWif, string? authenticationToken = null)
    {
        this.authenticationToken = authenticationToken ?? this.authenticationToken;
        this.privateKeyWIF = privateKeyWif;
    }

    public async Task<List<Contract>> GetContracts(string[] contractAddress)
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
                Arguments = $"status.mjs {authenticationToken} {ToCashAddr(contractAddress)} {privateKeyWIF}",
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

    public async Task<string[]> GetContractAddresses()
    {
        Network network = Network.Main;
        BitcoinSecret secret = new BitcoinSecret(privateKeyWIF, network);
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
                var transactions = data["transactions"];

                List<Task<string>> tasks = new List<Task<string>>();

                foreach (var tx in transactions)
                {
                    string txid = tx["hash"].ToString();

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
                                    return contractId;
                                }
                            }

                            return null;
                        }
                        else
                        {
                            throw new Exception($"Error fetching transaction {txid}: {txResponse.ReasonPhrase}");
                        }
                    }));
                }

                // Filter out null/empty results
                string[] contractIds = (await Task.WhenAll(tasks)).Where(id => !string.IsNullOrEmpty(id)).ToArray();
                return contractIds;
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