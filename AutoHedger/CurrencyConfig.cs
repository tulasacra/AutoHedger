namespace AutoHedger;

public class CurrencyConfig
{
    public WalletConfig Wallet;
    public string OracleKey;

    public CurrencyConfig(WalletConfig wallet)
    {
        Wallet = wallet;
        OracleKey = OracleKeys.Keys[wallet.Currency];
    }
}