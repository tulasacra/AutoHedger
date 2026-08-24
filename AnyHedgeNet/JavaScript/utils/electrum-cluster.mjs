import { ElectrumCluster, ElectrumTransport, ClusterOrder } from 'electrum-cash';
import { canonicalize, isElectrumTxList } from './electrum-integrity.mjs';

// electrum-cash default is 120s; blocked :50004 hosts then stall the whole cluster.
// Healthy servers connect in <200ms — 5s leaves headroom for slow links.
const CONNECTION_TIMEOUT_MS = 5000;

let integrityPatchInstalled = false;

// electrum-cash votes with JSON.stringify(response). Normalize UTXO/history lists so
// Rostrum extras and differing sort order don't break confidence.
function installElectrumIntegrityPatch()
{
	if(integrityPatchInstalled)
	{
		return;
	}
	integrityPatchInstalled = true;

	const originalRequest = ElectrumCluster.prototype.request;
	ElectrumCluster.prototype.request = async function patchedRequest(...args)
	{
		const nativeStringify = JSON.stringify.bind(JSON);
		JSON.stringify = (value, replacer, space) =>
		{
			if(isElectrumTxList(value))
			{
				return nativeStringify(canonicalize(value), replacer, space);
			}
			return nativeStringify(value, replacer, space);
		};
		try
		{
			return await originalRequest.apply(this, args);
		}
		finally
		{
			JSON.stringify = nativeStringify;
		}
	};
}

// :443 servers first — many networks block the traditional Electrum ports.
export function createMainnetElectrumCluster(applicationName = 'AnyHedge Application')
{
	installElectrumIntegrityPatch();

	// Initialise a 2-of-3 Electrum Cluster
	const electrum = new ElectrumCluster(applicationName, '1.4.1', 2, 3, ClusterOrder.PRIORITY, CONNECTION_TIMEOUT_MS);
	electrum.addServer('rostrum.riften.net', 443, ElectrumTransport.WSS.Scheme, false);
	electrum.addServer('fulcrum.pat.mn', 443, ElectrumTransport.WSS.Scheme, false);
	electrum.addServer('bch.imaginary.cash', 50004, ElectrumTransport.WSS.Scheme, false);
	electrum.addServer('blackie.c3-soft.com', 50004, ElectrumTransport.WSS.Scheme, false);
	electrum.addServer('electroncash.de', 60002, ElectrumTransport.WSS.Scheme, false);
	electrum.addServer('electroncash.dk', 50004, ElectrumTransport.WSS.Scheme, false);
	electrum.addServer('bch.loping.net', 50004, ElectrumTransport.WSS.Scheme, false);
	electrum.addServer('electrum.imaginary.cash', 50004, ElectrumTransport.WSS.Scheme, false);
	return electrum;
}
