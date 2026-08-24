// electrum-cash confidence compares JSON.stringify(response) exactly.
// Servers disagree on extra fields (Rostrum) and list order (history) — normalize both.

export function canonicalize(value)
{
	if(Array.isArray(value))
	{
		const items = value.map(canonicalize);
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
			const utxo =
			{
				height: value.height,
				tx_hash: value.tx_hash,
				tx_pos: value.tx_pos,
				value: value.value,
			};
			if(value.token_data !== undefined)
			{
				utxo.token_data = canonicalize(value.token_data);
			}
			return utxo;
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
