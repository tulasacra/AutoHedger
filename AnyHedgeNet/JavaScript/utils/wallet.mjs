// ...
import { DUST_LIMIT } from '@generalprotocols/anyhedge';

// This is intended to be used for funding transaction size estimation
const P2PKH_INPUT_SIZE = BigInt('148');

// ...
import { binToHex, sha256, ripemd160, secp256k1, decodePrivateKeyWif, encodeCashAddress, CashAddressType, encodeTransaction } from '@bitauth/libauth';

// Ooof! this is ugly..
import { createOutput, createUnsignedInput, estimateTransactionSizeWithoutInputs, signTransactionP2PKH } from '@generalprotocols/anyhedge/build/lib/util/funding-util.js';

// Parse a WIF string into a private key, public key and address.
export const parseWIF = async function(privateKeyWIF)
{
	// Attempt to decode WIF string into a private key
	const decodeResult = decodePrivateKeyWif(privateKeyWIF);

	// If decodeResult is a string, it represents an error, so we throw it.
	if(typeof decodeResult === 'string') throw(new Error(decodeResult));

	// Extract the private key from the decodeResult.
	const privateKeyBin = decodeResult.privateKey;

	// Derive the corresponding public key.
	const publicKeyBin = secp256k1.derivePublicKeyCompressed(privateKeyBin);

	// Hash the public key hash according to the P2PKH scheme.
	const publicKeyHashBin = ripemd160.hash(sha256.hash(publicKeyBin));

	// Encode the public key hash into a P2PKH cash address.
	const address = encodeCashAddress('bitcoincash', CashAddressType.p2pkh, publicKeyHashBin);

	return [ binToHex(privateKeyBin), binToHex(publicKeyBin), address ];
};

// Utility function that creates a new UTXO of en exact size needed for funding transactions.
export const buildPreFundingTransaction = async function(privateKeyWIF, unspentTransactionOutputs, fundingSatoshis)
{
	// Parse the private key and generate the intermediary address to use.
	const [ _privateKey, _publicKey, address ] = await parseWIF(privateKeyWIF);

	// Create a transaction output for the amount requested.
	const output = createOutput(address, fundingSatoshis);

	// Create a placeholder output representing a change output
	// NOTE: We set the placeholder satoshi amount to DUST_LIMIT, otherwise this function will throw an error.
	const placeholderChangeOutput = createOutput(address, DUST_LIMIT);

	// Generate unsigned transaction inputs for all UTXOs.
	const unsignedInputs = unspentTransactionOutputs.map((utxo) => createUnsignedInput(utxo));

	// Estimate what the transaction fee will be based on those two outputs and however many inputs.
	const transactionFee = estimateTransactionSizeWithoutInputs([ output, placeholderChangeOutput ]) + BigInt(unsignedInputs.length) * P2PKH_INPUT_SIZE;

	// Create an unsigned transaction containing all UTXOs as inputs and the requested output
	const unsignedTransaction =
	{
		version: 2,
		inputs: unsignedInputs,
		outputs: [ output ],
		locktime: 0,
	};

	// Calculate the total satoshi balance of the wallet
	const totalSatoshis = unspentTransactionOutputs.reduce((total, utxo) => total + utxo.satoshis, BigInt('0'));

	// Calculate the required satoshis to manufacture the requested UTXO + transaction fees
	const requiredSatoshis = fundingSatoshis + transactionFee;

	// Calculate the change satoshis
	const changeSatoshis = totalSatoshis - requiredSatoshis;

	// Negative change satoshis indicates the LP doesn't have enough funds - which is an internal error
	if(changeSatoshis < 0)
	{
		throw(new Error(`The provided taker private key/address (${address}) with balance ${totalSatoshis} does not have enough funds ${requiredSatoshis} to enter this position.`));
	}

	// Add a change output to the transaction if there is enough left
	if(changeSatoshis > DUST_LIMIT)
	{
		// Create and add the change output.
		const changeOutput = createOutput(address, changeSatoshis);
		unsignedTransaction.outputs.push(changeOutput);
	}

	// Sign the transaction
	const signedTransaction = await signTransactionP2PKH(privateKeyWIF, unsignedTransaction);

	// Encode the transaction
	const encodedTransaction = binToHex(encodeTransaction(signedTransaction));

	// Return the encoded transaction.
	return encodedTransaction;
}
