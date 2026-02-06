const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const http = require('http');

// Define Endpoint Configuration Locally
const ENDPOINT_URL = "http://localhost:5201/api/payments/card";
const METHOD = "POST";

function initiatePayment() {
    return new Promise((resolve, reject) => {
        // Load Config and Payload
        const configPath = path.join(__dirname, 'config.json');
        const payloadPath = path.join(__dirname, 'payload.json');
        
        // Try to load .env relative to this file
        const envPath = path.join(__dirname, '../../src/PaymentModule.Api/.env');

        if (!fs.existsSync(configPath) || !fs.existsSync(payloadPath)) {
            console.error("❌  Error: config.json or payload.json not found in " + __dirname);
            process.exit(1);
        }

        let config = JSON.parse(fs.readFileSync(configPath, 'utf8'));
        const payloadObj = JSON.parse(fs.readFileSync(payloadPath, 'utf8'));

        // Try to load credentials from .env if available
        if (fs.existsSync(envPath)) {
            try {
                const envContent = fs.readFileSync(envPath, 'utf8');
                const apiKeyMatch = envContent.match(/S2S_API_KEYS=(.+)/);
                const secretMatch = envContent.match(/S2S_HMAC_SECRETS=(.+)/);

                if (apiKeyMatch && apiKeyMatch[1]) {
                    config.apiKey = apiKeyMatch[1].split(',')[0].trim();
                }
                if (secretMatch && secretMatch[1]) {
                    config.secret = secretMatch[1].split(',')[0].trim();
                }
            } catch (err) {
                // Ignore .env errors
            }
        }

        const payloadString = JSON.stringify(payloadObj);

        // 1. Generate Headers
        const timestamp = Math.floor(Date.now() / 1000).toString();
        const nonce = crypto.randomUUID();
        const method = METHOD; // Use local constant

        const urlObj = new URL(ENDPOINT_URL); // Use local constant
        const urlPath = urlObj.pathname + urlObj.search;
        const canonicalString = `${method}${urlPath}${timestamp}${nonce}${payloadString}`;

        const hmac = crypto.createHmac('sha256', config.secret);
        hmac.update(canonicalString);
        const signature = hmac.digest('hex').toLowerCase();

        const options = {
            method: method,
            headers: {
                'Content-Type': 'application/json',
                'x-api-key': config.apiKey,
                'x-timestamp': timestamp,
                'x-nonce': nonce,
                'x-signature': signature
            }
        };
        
        console.log("\n🚀  Generated Headers:", options.headers);
        
        if (config.authToken) {
            options.headers['Authorization'] = `Bearer ${config.authToken}`;
        }

        console.log(`🚀  Initiating S2S Payment to ${ENDPOINT_URL}...`);

        const req = http.request(ENDPOINT_URL, options, (res) => {
            let responseBody = '';

            res.on('data', (chunk) => {
                responseBody += chunk;
            });

            res.on('end', () => {
                try {
                    const jsonResp = JSON.parse(responseBody);
                    if (res.statusCode >= 200 && res.statusCode < 300) {
                        console.log("\n✅  Payment Initiated Successfully!");
                        resolve(jsonResp);
                    } else {
                        console.log(`\n❌  S2S Failed [${res.statusCode}]`);
                        console.log(JSON.stringify(jsonResp, null, 2));
                        reject(new Error(`S2S Call Failed: ${res.statusCode}`));
                    }
                } catch (e) {
                    console.log(`\n❌  Raw Response: ${responseBody}`);
                    reject(e);
                }
            });
        });

        req.on('error', (e) => {
            console.error(`\n💥  Network Error: ${e.message}`);
            reject(e);
        });

        req.write(payloadString);
        req.end();
    });
}

// Allow running directly
if (require.main === module) {
    initiatePayment().then(res => {
        console.log("Response:", JSON.stringify(res, null, 2));
    }).catch(err => {
        process.exit(1);
    });
}

module.exports = { initiatePayment };
