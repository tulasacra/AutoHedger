/* eslint-disable no-console, no-use-before-define */

// Custodial example of establishing the parameters for an AnyHedge contract and funding it, then
// it will wait for one minute before it requests and completes an early settlement.

// Import library functions.
import { AnyHedgeManager } from '../build/lib/index.js';
import { hexToBin, hashTransaction } from '@bitauth/libauth';

// Import utility functions.
import { parseWIF, buildPreFundingTransaction } from './utils/wallet.js';
import { calculateRequiredFundingSatoshisPerSide } from './utils/anyhedge.js';
import { fetchJSONGetRequest, fetchJSONPostRequest, fetchCurrentOracleMessageAndSignature, fetchUnspentTransactionOutputs } from './utils/network.js';

// Set how many US cents that Short would like to protect against price volatility.
const NOMINAL_UNITS = 100;

// Set the oracle public key to one that you know is operational and available. This is the production USD price oracle.
const ORACLE_PUBLIC_KEY = '02d09db08af1ff4e8453919cc866a4be427d7bfe18f2c05e5444c196fcf6fd2818';

// Set the contract duration in seconds, after which the contract is matured.
// NOTE: 10800 = 3 hours * 60 minutes * 60 seconds.
// NOTE: Liquidity provider requires contracts to be at least a two hours long, and
//       won't settle contract early when they are less than two hours from maturity.
const CONTRACT_DURATION_IN_SECONDS = BigInt('10800');

// Set the multipliers for how much the price can change before the contract is liquidated.
// For example assuming the price today is $300 then:
//   if low multiplier = 0.75, the low liquidation price will be $300 * 0.75 = $225.
//   if high multiplier = 10, the high liquidation price will be $300 * 10 = $3,000.
const CONTRACT_LOW_LIQUIDATION_PRICE_MULTIPLIER = 0.75;
const CONTRACT_HIGH_LIQUIDATION_PRICE_MULTIPLIER = 10.00;

// The contract requires addresses for payout and public keys for validating mutual redemptions.
// Set these values to compressed WIF keys that you control and the example will use it for the public key and address.
// You can get WIF keys with a standard Electron Cash wallet.
// For safety, it is recommended to create a new wallet or use a testing wallet for this:
//   1. Go to the Addresses tab
//   2. Choose any address and take note of it so you can watch later for the automatic redemption to appear.
//   2. Right click the address --> Private Key
//   3. Copy the private key in the top box.
const TAKER_WIF = '';
const TAKER_SIDE = 'short';
const MAKER_SIDE = (TAKER_SIDE === 'short' ? 'long' : 'short');

// Select which liquidity provider to use as a counterparty.
// DEFAULT: https and liquidity.anyhedge.com
const LIQUIDITY_PROVIDER_SCHEME = 'https';
const LIQUIDITY_PROVIDER_DOMAIN = 'liquidity.anyhedge.com';
const LIQUIDITY_PROVIDER_PORT = 443;
const LIQUIDITY_PROVIDER_URL = `${LIQUIDITY_PROVIDER_SCHEME}://${LIQUIDITY_PROVIDER_DOMAIN}:${LIQUIDITY_PROVIDER_PORT}`;

// To use the automated redemption service, you need to request an authentication token from the service provider.
// Request a token once by running the following command in the terminal:
// curl -d 'name=<Your Name Here>' "https://api.anyhedge.com/api/v2/requestToken"
const AUTHENTICATION_TOKEN = '';

// Name a value that can be used as an integer-based boolean (as contracts do)
const INTEGER_TRUE = BigInt('1');

// Wrap the example code in an async function to allow async/await.
const example = async function()
{
	// Get service information from the liquidity provider.
	const liquidityServiceInformationUrl = `${LIQUIDITY_PROVIDER_URL}/api/v2/liquidityServiceInformation`;
	const liquidityServiceInformationResponse = await fetchJSONGetRequest(liquidityServiceInformationUrl);

	// Extract the settlement service, oracle relay and liquidity parameters.
	const { settlementService, oracleRelay, liquidityParameters } = liquidityServiceInformationResponse;

	// NOTE: Regular clients should validate that user provided input fit within the liquidity service constraints in the liquidityParameters.

	// Create an instance of the AnyHedge manager using the provided settlement service.
	const anyHedgeManager = new AnyHedgeManager({ serviceScheme: settlementService.scheme, serviceDomain: settlementService.host, servicePort: settlementService.port, authenticationToken: AUTHENTICATION_TOKEN });

	// Define url and arguments needed to prepare a contract position, which will give us necessary details about the liquidity providers side of the contract.
	const prepareContractPositionUrl = `${LIQUIDITY_PROVIDER_URL}/api/v2/prepareContractPosition`;
	const prepareContractPositionArguments =
	{
		oraclePublicKey: ORACLE_PUBLIC_KEY,
		poolSide: MAKER_SIDE,
	};

	// Fetch liquidity provider information required in order to make a new contract position.
	const prepareContractPositionResponse = await fetchJSONPostRequest(prepareContractPositionUrl, prepareContractPositionArguments);

	// Extract the liquidity providers public key, payout address and available satoshis.
	const { liquidityProvidersMutualRedemptionPublicKey, liquidityProvidersPayoutAddress, availableLiquidityInSatoshis } = prepareContractPositionResponse;

	// Collect all the parameters that we need to create a contract
	const [ startingOracleMessage, startingOracleSignature ] = await fetchCurrentOracleMessageAndSignature(ORACLE_PUBLIC_KEY, oracleRelay.host);
	const [ _takerPrivateKey, takerMutualRedeemPublicKey, takerPayoutAddress ] = await parseWIF(TAKER_WIF);

	// Allow mutual redemptions for this contract.
	const enableMutualRedemption = INTEGER_TRUE;

	// Determine the short mutual redemption public key and payout address.
	const shortMutualRedeemPublicKey = (TAKER_SIDE === 'short' ? takerMutualRedeemPublicKey : liquidityProvidersMutualRedemptionPublicKey);
	const shortPayoutAddress         = (TAKER_SIDE === 'short' ? takerPayoutAddress : liquidityProvidersPayoutAddress);

	// Determine the long mutual redemption public key and payout address.
	const longMutualRedeemPublicKey = (TAKER_SIDE === 'long' ? takerMutualRedeemPublicKey : liquidityProvidersMutualRedemptionPublicKey);
	const longPayoutAddress         = (TAKER_SIDE === 'long' ? takerPayoutAddress : liquidityProvidersPayoutAddress);

	// Calculate the maturity timestamp based on the contract duration.
	const maturityTimestamp = BigInt(Math.ceil((Date.now() / 1000))) + CONTRACT_DURATION_IN_SECONDS;

	// Gather all contract creation parameters.
	const contractCreationParameters =
	{
		takerSide: TAKER_SIDE,
		makerSide: MAKER_SIDE,
		oraclePublicKey: ORACLE_PUBLIC_KEY,
		shortMutualRedeemPublicKey,
		longMutualRedeemPublicKey,
		shortPayoutAddress,
		longPayoutAddress,
		enableMutualRedemption,
		nominalUnits: NOMINAL_UNITS,
		startingOracleMessage,
		startingOracleSignature,
		maturityTimestamp,
		isSimpleHedge: INTEGER_TRUE,
		highLiquidationPriceMultiplier: CONTRACT_HIGH_LIQUIDATION_PRICE_MULTIPLIER,
		lowLiquidationPriceMultiplier: CONTRACT_LOW_LIQUIDATION_PRICE_MULTIPLIER,
	};

	// Define url and package the arguments needed to propose a contract position.
	const proposeContractUrl = `${LIQUIDITY_PROVIDER_URL}/api/v2/proposeContract`;
	const proposeContractArguments = { contractCreationParameters };

	// Send the contract proposal to the liquidity provider.
	const proposeContractResponse = await fetchJSONPostRequest(proposeContractUrl, proposeContractArguments);

	// Extract the liquidity provider fee, the duration the offer is valid for or the available liquidity if no offer was made.
	const { liquidityProviderFeeInSatoshis, renegotiateAfterTimestamp, availableLiquidityInSatoshis: updatedAvailableLiquidityInSatoshis } = proposeContractResponse;

	// Throw an error if the liquidity provider does not have sufficient liquidity available for the contract.
	if(typeof updatedAvailableLiquidityInSatoshis !== 'undefined')
	{
		throw(new Error(`Unable to create contract, available liquidity (${updatedAvailableLiquidityInSatoshis}) is insufficient`));
	}

	// NOTE: Regular clients should take note of the renegotiateAfterTimestamp entry and manage their next step so that it happens within the required timeframe.

	// Retrieve contract data from the settlement service to get the final contract information.
	// NOTE: This step is necessary because the settlement service adds a fee that needs to be taken into consideration when funding the contract.
	const { address } = await anyHedgeManager.createContract(contractCreationParameters);
	const pendingContractData = await anyHedgeManager.getContractStatus(address, TAKER_WIF);

	// Calculate how many satoshis the taker needs to prepare for the contract.
	// NOTE: The settlement service fee, and any potential other fees in other scenarios, exist in the fee structure within the pending contract data.
	// NOTE: Since the premium from the liquidity provider is not always paid out of bound, we need a special function to manage the liquidity provider fee.
	const { takerInputSatoshis } = await calculateRequiredFundingSatoshisPerSide(pendingContractData, TAKER_SIDE, liquidityProviderFeeInSatoshis);

	// Fetch all available UTXOs.
	// NOTE: This will result in automatic consolidation of UTXOs and regular clients should implement their own coin selection strategies.
	const unspentTransactionOutputs = await fetchUnspentTransactionOutputs(takerPayoutAddress);

	// Create a dependency transaction used to manufacture a UTXO of the correct amount
	const dependencyTransaction = await buildPreFundingTransaction(TAKER_WIF, unspentTransactionOutputs, takerInputSatoshis);

	// Hash the dependency transaction.
	const dependencyTransactionHash = hashTransaction(hexToBin(dependencyTransaction));

	// Create an unsigned proposal using the manufactured UTXO (by convention at the 0th output)
	const unsignedProposal = anyHedgeManager.createFundingProposal(pendingContractData, dependencyTransactionHash, 0, takerInputSatoshis);

	// Sign the proposal.
	const signedProposal = await anyHedgeManager.signFundingProposal(TAKER_WIF, unsignedProposal);

	// Define url and arguments needed to fund the contract position.
	const fundContractUrl = `${LIQUIDITY_PROVIDER_URL}/api/v2/fundContract`;
	const fundContractArguments =
	{
		contractAddress: pendingContractData.address,
		outpointTransactionHash: dependencyTransactionHash,
		outpointIndex: 0,
		satoshis: takerInputSatoshis,
		signature: signedProposal.signature,
		publicKey: signedProposal.publicKey,
		dependencyTransactions: [ dependencyTransaction ],
	};

	// Send the contract funding data to the liquidity provider to complete the funding.
	const fundContractResponse =  await fetchJSONPostRequest(fundContractUrl, fundContractArguments);

	// Throw an error if funding failed.
	if(typeof fundContractResponse === 'string')
	{
		throw(new Error(`Failed to fund contract: ${fundContractResponse}`));
	}

	// Extract the funding transaction hash from the response.
	const { fundingTransactionHash } = fundContractResponse;

	console.log(`Funded contract '${pendingContractData.address}' in transaction '${fundingTransactionHash}'.`);

	// Sleep for a minute.
	// NOTE: this allows funding transaction to fully propagate and gets a new oracle price for the settlement.
	// NOTE: the oracle price used can still be the same, if price has not moved, and it can even be the same message if no new message has been issued.
	console.log(`\nWaiting 1 minute...\n`);
	await new Promise(resolve => setTimeout(resolve, 60 * 1000));

	// Grab updated contract data from settlement service.
	const fundedContractData = await anyHedgeManager.getContractStatus(address, TAKER_WIF);

	// Fetch new oracle messages to use as the early settlement price point.
	const [ settlementOracleMessage, settlementOracleSignature ] = await fetchCurrentOracleMessageAndSignature(ORACLE_PUBLIC_KEY, oracleRelay.host);

	// Define url and arguments needed to request early settlement of our position.
	const requestEarlySettlementUrl = `${LIQUIDITY_PROVIDER_URL}/api/v2/requestEarlySettlement`;
	const requestEarlySettlementArguments =
	{
		contractAddress: pendingContractData.address,
		settlementOracleMessage,
		settlementOracleSignature,
	};

	// Send a request to the liquidity provider to settle the contract position early.
	const requestEarlySettlementResponse = await fetchJSONPostRequest(requestEarlySettlementUrl, requestEarlySettlementArguments);

	// Extract the settlement price, fee and timeout information from the response.
	const { settlementPrice, settlementFeeInSatoshis, renegotiateAfterTimestamp: renegotiateSettlementAfterTimestamp } = requestEarlySettlementResponse;

	// NOTE: We will assume all values provided as sensible and desired, but ideally this is data that users should manually inspect.

	// Extract the funded contracts parameters, metadata and funding information.
	const { parameters: contractParameters, metadata: contractMetadata, fundings } = fundedContractData;

	// sign a mutual early maturation proposal, taking the liquidity provides settlement fee into account.
	const takerEarlySettlementProposal = await anyHedgeManager.signMutualEarlyMaturation({ privateKeyWIF: TAKER_WIF, contractFunding: fundings[0], contractParameters, contractMetadata, settlementPrice, takerSettlementFeeInSatoshis: settlementFeeInSatoshis });

	// Define url and arguments needed to complete early settlement of our position.
	const completeEarlySettlementUrl = `${LIQUIDITY_PROVIDER_URL}/api/v2/settleContractEarly`;
	const completeEarlySettlementArguments =
	{
		contractAddress: pendingContractData.address,
		takerEarlySettlementProposal,
	};

	// Send the partially signed early termination request to the liquidity provider for completion.
	const completeEarlySettlementResponse = await fetchJSONPostRequest(completeEarlySettlementUrl, completeEarlySettlementArguments);

	// Extract the settlement transaction hash for the early settlement.
	const { settlementTransactionHash } = completeEarlySettlementResponse;

	console.log(`Settled contract '${pendingContractData.address}' early in transaction '${settlementTransactionHash}'.`);
};

// Run the example code.
example();
