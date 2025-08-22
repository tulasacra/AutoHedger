// Import fetch to get and post requests.
//import fetch from 'node-fetch';

// Import oracle related utilities.
import { OracleClient, OracleMessage } from '@generalprotocols/oracle-client';

// Import electrum related utilities.
import { ElectrumNetworkProvider } from 'cashscript';

// Load support for bigint in JSON
import { decodeExtendedJson, encodeExtendedJson } from '@generalprotocols/anyhedge';

// Define a variable to hold a local instance of the network provider.
let electrumNetworkProvider;

// Wrapper function to ensure we use the same instance of the local network provider in all calls.
const getLocalNetworkProviderInstance = async function()
{
	// Create a local instance of the ElectrumNetworkProvider, if necessary
	if(!electrumNetworkProvider)
	{
		electrumNetworkProvider = new ElectrumNetworkProvider();
	}

	// Return the local instance of the network provider.
	return electrumNetworkProvider;
}

// Get the current oracle price to use as starting and ending conditions for an contract.
export const fetchCurrentOracleMessageAndSignature = async function(oraclePublicKey, oracleRelayHost, oracleRelayPort = 443)
{
	let oracleClient;
	try
	{
		const baseURL = `https://${oracleRelayHost}:${oracleRelayPort}`;
		oracleClient = await OracleClient.from(baseURL);

		// Define the search parameters to request the most recent price for the oracle.
		// NOTE: minDataSequence needs to be 1 in order to disregard the latest message(s) if they happens to be metadata messages.
		const searchRequest =
		{
			publicKey: oraclePublicKey,
			minDataSequence: 1,
			count: 1,
		};

		// Request the latest price as specified in the search parameters.
		const requestedMessages = await oracleClient.getOracleMessages(searchRequest);

		if(!requestedMessages || requestedMessages.length === 0)
		{
			throw(new Error('Could not get current oracle message: no messages returned by the oracle relay.'));
		}

		// Extract the latest message from the response.
		const { message, signature, publicKey } = requestedMessages[0].toHexObject();

		/*
		// Verify an oracle price message signature.
		const validMessageSignature = await OracleData.verifyMessageSignature(hexToBin(message), hexToBin(signature), hexToBin(publicKey));

		// Throw an error if the signature could not be properly validated.
		if(!validMessageSignature)
		{
			throw(new Error('Could not get current oracle message: the oracle relay provided an invalid signature for the message.'));
		}
		*/

		// Return starting oracle message and signature.
		return [ message, signature ];
	}
	catch(error)
	{
		throw(new Error(`Failed to fetch oracle data: ${error.message}`));
	}
	finally
	{
		// Make sure to clean up the SSE connection
		try
		{
			if(oracleClient)
			{
				await oracleClient.stop();
			}
		}
		catch(cleanupError)
		{
			// Ignore cleanup errors
		}
	}
};

// Fetch the list of current UTXOs for an address.
export const fetchUnspentTransactionOutputs = async function(address)
{
	// Get the local instance of the network provider.
	const electrumNetworkProvider = await getLocalNetworkProviderInstance();

	// Retrieve all unspent outputs for the takers address
	const unspentOutputs = await electrumNetworkProvider.getUtxos(address);

	return unspentOutputs;
}

// Wrapper function to simplify fetch usage when making GET calls with a JSON response.
export const fetchJSONGetRequest = async function(url)
{
	const response = await fetch(url);

	return decodeExtendedJson(await response.text());
}

// Wrapper function to simplify fetch usage when making POST calls with a JSON response.
export const fetchJSONPostRequest = async function(url, data)
{
	const options =
	{
		method: "POST",
		cache: "no-cache",
		headers: { "Content-Type": "application/json" },
		body: encodeExtendedJson(data),
	};

	const response = await fetch(url, options);

	return decodeExtendedJson(await response.text());
}
