import { calculateRequiredFundingSatoshis } from '@generalprotocols/anyhedge/build/lib/util/funding-util.js';

// ...
export const calculateRequiredFundingSatoshisPerSide = async function(contractData, takerSide, liquidityProviderFeeInSatoshis)
{
	// Determine the total required satoshis for the contract to operate properly, including all fees.
	const totalRequiredSatoshis = calculateRequiredFundingSatoshis(contractData);

	// Get the base maker input for the contract value only, making the initial assumption that maker pays no fees.
	const makerInputSatoshisBeforeLiquidityFee = (takerSide === 'short' ? contractData.metadata.longInputInSatoshis : contractData.metadata.shortInputInSatoshis);

	// Get the taker input, making the initial assumption that taker pays for all fees.
	const takerInputSatoshisBeforeLiquidityFee = (totalRequiredSatoshis - makerInputSatoshisBeforeLiquidityFee);

	// In the case that the liquidity provider fee is non-negative, then the assumptions above are correct
	// and the value below will be zero, causing no further change.
	// In the case that the liquidity provider fee is negative, we have to correct the assumption and shift
	// the liquidity provider fee from the taker to the maker by subtracting from taker and adding to maker.
	const negativeLiquidityProviderFeeInSatoshis = BigInt(liquidityProviderFeeInSatoshis < 0 ? Math.abs(liquidityProviderFeeInSatoshis) : 0);

	// Add the liquidity provider premium to the paying side, if any.
	const makerInputSatoshis = makerInputSatoshisBeforeLiquidityFee + negativeLiquidityProviderFeeInSatoshis;
	const takerInputSatoshis = takerInputSatoshisBeforeLiquidityFee - negativeLiquidityProviderFeeInSatoshis;

	// Return both maker and taker input satoshis.
	return { makerInputSatoshis, takerInputSatoshis };
}
