import { ElectrumCluster, ElectrumTransport, ClusterOrder } from 'electrum-cash';

const address = process.argv[2];

class ElectrumNetworkProvider {
    constructor(network = 'mainnet', electrum, manualConnectionManagement) {
        this.network = network;
        this.manualConnectionManagement = manualConnectionManagement;
        this.concurrentRequests = 0;

        // If a custom Electrum Cluster is passed, we use it instead of the default.
        if (electrum) {
            this.electrum = electrum;
            return;
        }

        if (network === 'mainnet') {
            // Initialise a 2-of-3 Electrum Cluster with 6 reliable hardcoded servers
            this.electrum = new ElectrumCluster('AnyHedge Application', '1.4.1', 2, 3, ClusterOrder.PRIORITY);
            this.electrum.addServer('bch.imaginary.cash', 50004, ElectrumTransport.WSS.Scheme, false);
            this.electrum.addServer('blackie.c3-soft.com', 50004, ElectrumTransport.WSS.Scheme, false);
            this.electrum.addServer('electroncash.de', 60002, ElectrumTransport.WSS.Scheme, false);
            this.electrum.addServer('electroncash.dk', 50004, ElectrumTransport.WSS.Scheme, false);
            this.electrum.addServer('bch.loping.net', 50004, ElectrumTransport.WSS.Scheme, false);
            this.electrum.addServer('electrum.imaginary.cash', 50004, ElectrumTransport.WSS.Scheme, false);
        } else {
            throw new Error(`Tried to instantiate an ElectrumNetworkProvider for unsupported network ${network}`);
        }
    }

    async connectCluster() {
        try {
            return await this.electrum.startup();
        } catch (e) {
            return [];
        }
    }

    async disconnectCluster() {
        return this.electrum.shutdown();
    }

    async performRequest(name, ...parameters) {
        // Only connect the cluster when no concurrent requests are running
        if (this.shouldConnect()) {
            this.connectCluster();
        }

        this.concurrentRequests += 1;
        await this.electrum.ready();

        let result;
        try {
            result = await this.electrum.request(name, ...parameters);
        } finally {
            // Always disconnect the cluster, also if the request fails
            // as long as no other concurrent requests are running
            if (this.shouldDisconnect()) {
                await this.disconnectCluster();
            }
        }

        this.concurrentRequests -= 1;
        if (result instanceof Error) throw result;
        return result;
    }

    shouldConnect() {
        if (this.manualConnectionManagement) return false;
        if (this.concurrentRequests !== 0) return false;
        return true;
    }

    shouldDisconnect() {
        if (this.manualConnectionManagement) return false;
        if (this.concurrentRequests !== 1) return false;
        return true;
    }

    async getTransactionHistory(address) {
        return await this.performRequest('blockchain.address.get_history', address);
    }
}

async function fetchTransactionIds(address) {
    // Create an Electrum network provider
    const electrum = new ElectrumNetworkProvider('mainnet');
    
    try {
        // Get transaction history for the address
        const history = await electrum.getTransactionHistory(address);

        // Extract transaction IDs from history
        const txIds = history.map(item => item.tx_hash);

        return txIds;
    } catch (error) {
        console.error('Error fetching transaction history:', error);
        throw error;
    }
}

const transactionIds = await fetchTransactionIds(address);
console.log(JSON.stringify(transactionIds));