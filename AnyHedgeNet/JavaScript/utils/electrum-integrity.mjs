// electrum-cash confidence compares JSON.stringify(response) exactly.
// Servers disagree on extra fields (Rostrum), list order (history), and whether
// CashToken UTXOs appear in listunspent (Rostrum includes them, Fulcrum omits).

export function canonicalize(value)
{
	if(Array.isArray(value))
	{
		let items = value;
		if(items.length > 0 && isUtxoShape(items[0]))
		{
			// Drop token UTXOs before field-stripping so Rostrum/Fulcrum lists match.
			items = items.filter((utxo) => !isTokenUtxo(utxo));
		}

		items = items.map(canonicalize);
		if(items.length > 0 && isUtxoShape(items[0]))
		{
			items.sort((a, b) => `${a.tx_hash}:${a.tx_pos}`.localeCompare(`${b.tx_hash}:${b.tx_pos}`));
		}
		else if(items.length > 0 && isHistoryShape(items[0]))
		{
			items.sort((a, b) =>
				a.height !== b.height
					? a.height - b.height
					: a.tx_hash.localeCompare(b.tx_hash));
		}
		return items;
	}

	if(value !== null && typeof value === 'object')
	{
		if(isUtxoShape(value))
		{
			// Omit Rostrum-only extras (has_token, outpoint_hash, token_data).
			return {
				height: value.height,
				tx_hash: value.tx_hash,
				tx_pos: value.tx_pos,
				value: value.value,
			};
		}

		if(isHistoryShape(value))
		{
			return {
				height: value.height,
				tx_hash: value.tx_hash,
			};
		}

		const out = {};
		for(const key of Object.keys(value).sort())
		{
			out[key] = canonicalize(value[key]);
		}
		return out;
	}

	return value;
}

function isTokenUtxo(value)
{
	return value !== null
		&& typeof value === 'object'
		&& (value.has_token === true || value.token_data != null);
}

// listunspent entries
function isUtxoShape(value)
{
	return value !== null
		&& typeof value === 'object'
		&& Object.prototype.hasOwnProperty.call(value, 'tx_hash')
		&& Object.prototype.hasOwnProperty.call(value, 'tx_pos');
}

// get_history entries (tx_hash + height, no tx_pos)
function isHistoryShape(value)
{
	return value !== null
		&& typeof value === 'object'
		&& Object.prototype.hasOwnProperty.call(value, 'tx_hash')
		&& Object.prototype.hasOwnProperty.call(value, 'height')
		&& !Object.prototype.hasOwnProperty.call(value, 'tx_pos');
}

export function isElectrumTxList(value)
{
	return Array.isArray(value)
		&& value.length > 0
		&& value[0] !== null
		&& typeof value[0] === 'object'
		&& Object.prototype.hasOwnProperty.call(value[0], 'tx_hash');
}
