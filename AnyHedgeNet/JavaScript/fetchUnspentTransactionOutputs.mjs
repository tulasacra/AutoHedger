import { fetchUnspentTransactionOutputs } from './utils/network.mjs';

const addresses = process.argv[2].split(',');

const replaceBigInt = (key, value) =>
    typeof value === 'bigint' ? value.toString() : value;

const unspentTransactionOutputs = await Promise.all(
    addresses.map(address => fetchUnspentTransactionOutputs(address))
);
console.log(JSON.stringify(unspentTransactionOutputs, replaceBigInt));