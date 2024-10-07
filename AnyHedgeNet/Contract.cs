using System.Numerics;
using Newtonsoft.Json;

namespace AnyHedgeNet;

public class Contract
{
    public string Version { get; set; }
    public string Address { get; set; }
    public Parameters Parameters { get; set; }
    public Metadata Metadata { get; set; }
    public List<Funding> Fundings { get; set; }
    public List<Fee> Fees { get; set; }
}

public class Parameters
{
    public BigInteger MaturityTimestamp { get; set; }
    public BigInteger StartTimestamp { get; set; }
    public BigInteger HighLiquidationPrice { get; set; }
    public BigInteger LowLiquidationPrice { get; set; }
    public BigInteger PayoutSats { get; set; }
    public BigInteger NominalUnitsXSatsPerBch { get; set; }
    public BigInteger SatsForNominalUnitsAtHighLiquidation { get; set; }
    public string OraclePublicKey { get; set; }
    public string LongLockScript { get; set; }
    public string ShortLockScript { get; set; }
    public BigInteger EnableMutualRedemption { get; set; }
    public string LongMutualRedeemPublicKey { get; set; }
    public string ShortMutualRedeemPublicKey { get; set; }
}

public class Metadata
{
    public string TakerSide { get; set; }
    public string MakerSide { get; set; }
    public string ShortPayoutAddress { get; set; }
    public string LongPayoutAddress { get; set; }
    public string StartingOracleMessage { get; set; }
    public string StartingOracleSignature { get; set; }
    public BigInteger DurationInSeconds { get; set; }
    public decimal HighLiquidationPriceMultiplier { get; set; }
    public decimal LowLiquidationPriceMultiplier { get; set; }
    public BigInteger IsSimpleHedge { get; set; }
    public BigInteger StartPrice { get; set; }
    public decimal NominalUnits { get; set; }
    public decimal ShortInputInOracleUnits { get; set; }
    public decimal LongInputInOracleUnits { get; set; }
    public BigInteger ShortInputInSatoshis { get; set; }
    public BigInteger LongInputInSatoshis { get; set; }
    public BigInteger MinerCostInSatoshis { get; set; }
}

public class Funding
{
    public string FundingTransactionHash { get; set; }
    public BigInteger FundingOutputIndex { get; set; }
    public BigInteger FundingSatoshis { get; set; }
    public Settlement? Settlement;
}

public class Settlement
{
    public string SettlementTransactionHash { get; set; }
    public string SettlementType { get; set; }
    public int SettlementPrice { get; set; }
    public string SettlementMessage { get; set; }
    public string SettlementSignature { get; set; }
    public string PreviousMessage { get; set; }
    public string PreviousSignature { get; set; }
    public long ShortPayoutInSatoshis { get; set; }
    public long LongPayoutInSatoshis { get; set; }
}

public class Fee
{
    public string Address { get; set; }
    public BigInteger Satoshis { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
}