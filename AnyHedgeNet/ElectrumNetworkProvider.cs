using System.Text;
using System.Diagnostics;
using System.Text.Json;

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
        StringBuilder resultBuilder = new();
        process.OutputDataReceived += (sender, args) => { resultBuilder.AppendLine(args.Data); };
        process.Start();
        process.BeginOutputReadLine();
        
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch (TimeoutException)
        {
            process.Kill();
            throw new TimeoutException("Process execution timed out.");
        }

        string result = resultBuilder.ToString();
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

    public static async Task<List<string>> GetTxIds(string address)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                WorkingDirectory = "JavaScript",
                FileName = "node",
                Arguments = $"getHistory.mjs {address}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        StringBuilder resultBuilder = new();
        process.OutputDataReceived += (sender, args) => { resultBuilder.AppendLine(args.Data); };
        process.Start();
        process.BeginOutputReadLine();
        
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch (TimeoutException)
        {
            process.Kill();
            throw new TimeoutException("Process execution timed out.");
        }

        string result = resultBuilder.ToString();
        string error = await process.StandardError.ReadToEndAsync();

        if (process.ExitCode != 0)
        {
            throw new Exception($"{error}{Environment.NewLine}{result}");
        }

        List<string> transactionIds = System.Text.Json.JsonSerializer.Deserialize<List<string>>(result);
        
        return transactionIds;
    }
}