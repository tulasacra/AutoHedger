namespace AutoHedger.Tests;

[TestFixture]
[TestOf(typeof(ElectrumNetworkProvider))]
public class ElectrumNetworkProviderTest
{

    [Test]
    public async Task GetTxIds()
    {
        var result = await ElectrumNetworkProvider.GetTxIds("bitcoincash:ppwk8u8cg8cthr3jg0czzays6hsnysykes9amw07kv");
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count(), Is.GreaterThanOrEqualTo(345));
    }
}