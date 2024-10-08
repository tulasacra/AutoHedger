// Load the AnyHedge library.
import { AnyHedgeManager } from '@generalprotocols/anyhedge';

// Contract address for the contract you want to get status for.
const CONTRACT_ADDRESS = process.argv[3];
const privateKeyWIF = process.argv[4];

// Set which settlement service to use in this example.
// DEFAULT: https and api.anyhedge.com
const SETTLEMENT_SERVICE_SCHEME = 'https';
const SETTLEMENT_SERVICE_DOMAIN = 'api.anyhedge.com';
const SETTLEMENT_SERVICE_PORT   = 443;

// To use the automated redemption service, you need to request an authentication token from the service provider.
// Request a token once by running the following command in the terminal:
// curl -d 'name=<Your Name Here>' "https://api.anyhedge.com/api/v2/requestToken"
const AUTHENTICATION_TOKEN = process.argv[2];

// Create an instance of the AnyHedge manager using the authentication token.
const anyHedgeManager = new AnyHedgeManager({ serviceDomain: SETTLEMENT_SERVICE_DOMAIN, serviceScheme: SETTLEMENT_SERVICE_SCHEME, servicePort: SETTLEMENT_SERVICE_PORT, authenticationToken: AUTHENTICATION_TOKEN });

// Wrap the example code in an async function to allow async/await.
const example = async function()
{
	const contractData = await anyHedgeManager.getContractStatus(CONTRACT_ADDRESS, privateKeyWIF);
	
	// Have to do this to prevent "TypeError: Do not know how to serialize a BigInt"
	// It also solves the problem of JSON libs unable to parse numbers in the 12345n format.
	const replaceBigInt = (key, value) =>
		typeof value === 'bigint' ? value.toString() : value;
	
	// Can't do simple console.log(contractData)
	// Otherwise there is just "settlement: [Object]"
	console.log(JSON.stringify(contractData, replaceBigInt))
};

await example();
