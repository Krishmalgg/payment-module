const { initiatePayment } = require('./initiate-payment');
const http = require('http');
const querystring = require('querystring');

// Configuration for Webhook Attack
const WEBHOOK_HOST = 'localhost';
const WEBHOOK_PORT = 5025; // Payment Server Port
const WEBHOOK_PATH = '/api/v1/webhooks/payhere';

async function runTest() {
    try {
        console.log("--- Step 1: Initiate Valid Payment (Main Server) ---");
        // This will call the Main Server (5201) as defined in initiate-payment.js
        const initResult = await initiatePayment();
        
        // Extract IDs
        // Based on Step 180 output, the structure is: { success: true, transactionId: "...", paymentDetails: { orderId: "..." } }
        let orderId = initResult.transactionId;
        
        // Prefer orderId from paymentDetails if available, as that's what PayHere uses
        if (initResult.paymentDetails && initResult.paymentDetails.orderId) {
            orderId = initResult.paymentDetails.orderId;
        }

        if (!orderId) {
            console.error("❌  Could not find Order ID in response:", initResult);
            process.exit(1);
        }
        
        console.log(`\n✅  Got Order ID: ${orderId}`);
        
        console.log(`\n--- Step 2: Send Invalid Webhook (Bad MD5) to ${WEBHOOK_HOST}:${WEBHOOK_PORT} ---`);
        
        // Construct Webhook Payload
        // Fields based on PayHereAdapter expectations: merchant_id, order_id, payhere_amount, payhere_currency, status_code, md5sig
        const postData = querystring.stringify({
            'merchant_id': '1233166',        // Should match what your server expects/configured
            'order_id': orderId,
            'payment_id': 'PID_ATTACK_TEST',
            'payhere_amount': '1500.00',     // Match initiate amount
            'payhere_currency': 'LKR',
            'status_code': '2',              // Success status
            'md5sig': 'INVALID_SIGNATURE_FOR_TESTING', // <--- THE ATTACK
            'method': 'VISA',
            'status_message': 'Mock Payment',
            'custom_1': '',
            'custom_2': ''
        });

        const options = {
            hostname: WEBHOOK_HOST,
            port: WEBHOOK_PORT, 
            path: WEBHOOK_PATH,
            method: 'POST',
            headers: {
                'Content-Type': 'application/x-www-form-urlencoded',
                'Content-Length': Buffer.byteLength(postData)
            }
        };

        const req = http.request(options, (res) => {
            let responseBody = '';
            res.on('data', (chunk) => responseBody += chunk);
            res.on('end', () => {
                console.log(`\n📨  Webhook Response [${res.statusCode}]:`);
                console.log(responseBody);
                
                if (res.statusCode === 200) {
                    console.log("\n✅  Webhook Accepted.");
                    console.log("👉  Action Required: Check Payment Server logs (dotnet run console).");
                    console.log("    Look for: '[SECURITY ALERT] MD5 Signature Mismatch'");
                } else {
                    console.log("\n⚠️  Unexpected Response code.");
                }
            });
        });

        req.on('error', (e) => {
            console.error(`problem with request: ${e.message}`);
        });

        req.write(postData);
        req.end();

    } catch (err) {
        console.error("Test Failed:", err);
    }
}

runTest();
