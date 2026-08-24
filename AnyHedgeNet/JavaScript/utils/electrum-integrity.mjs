// electrum-cash confidence compares JSON.stringify(response) exactly.
// Rostrum adds fields Fulcrum omits (has_token, outpoint_hash) — strip those for the vote.

export function canonicalize(value)
{
	if(Array.isArray(value))
	{
		const items = value.map(canonicalize);
		if(items.length > 0 && isUtxoShape(items[0]))
		{
			items.sort((a, b) => `${a.tx_hash}:${a.tx_pos}`.localeCompare(`${b.tx_hash}:${b.tx_pos}`));
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

		const out = {};
		for(const key of Object.keys(value).sort())
		{
			out[key] = canonicalize(value[key]);
		}
		return out;
	}

	return value;
}

function isUtxoShape(value)
{
	return value !== null
		&& typeof value === 'object'
		&& Object.prototype.hasOwnProperty.call(value, 'tx_hash')
		&& Object.prototype.hasOwnProperty.call(value, 'tx_pos');
}
