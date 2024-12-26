namespace AnyHedgeNet;

public class UTXO
{
    public string Txid { get; set; }
    public int Vout { get; set; }
    public long Satoshis { get; set; }
}

public class ElectrumNetworkProvider
{
    private const decimal _satsPerBch = 100_000_000m;

    public static async Task<Dictionary<string, List<UTXO>>> GetUTXOs(List<string> addresses)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                WorkingDirectory = "JavaScript",
                FileName = "node",
                Arguments = $"fetchUnspentTransactionOutputs.mjs {string.Join(',', addresses)}",
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

        var UTXOs = JsonConvert.DeserializeObject<List<List<UTXO>>>(result);
        return addresses.Zip(UTXOs).ToDictionary(tuple => tuple.First, tuple => tuple.Second);
    }

    public static async Task<Dictionary<string, decimal>> GetBalanceBCH(List<string> addresses)
    {
        var UTXOs = await GetUTXOs(addresses);
        var balances = new Dictionary<string, decimal>();

        foreach (var address in addresses)
        {
            balances[address] = UTXOs[address].Sum(utxo => utxo.Satoshis) / _satsPerBch;
        }

        return balances;
    }
}