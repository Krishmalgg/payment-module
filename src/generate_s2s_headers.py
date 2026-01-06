import hmac
import hashlib
import time
import uuid
import sys
import json
import argparse

def generate_headers(api_key, secret, method, url_path, body):
    timestamp = str(int(time.time()))
    nonce = str(uuid.uuid4())
    
    # Ensure method is uppercase
    method = method.upper()
    
    # Canonical string construction: Method + Path + Timestamp + Nonce + Body
    # Note: url_path should include query string if any
    payload = f"{method}{url_path}{timestamp}{nonce}{body}"
    
    # Secret to bytes
    secret_bytes = secret.encode('utf-8')
    payload_bytes = payload.encode('utf-8')
    
    # Compute HMAC
    signature = hmac.new(secret_bytes, payload_bytes, hashlib.sha256).hexdigest().lower()
    
    return {
        "x-api-key": api_key,
        "x-timestamp": timestamp,
        "x-nonce": nonce,
        "x-signature": signature
    }

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description='Generate S2S Headers')
    parser.add_argument('--apikey', required=True, help='API Key')
    parser.add_argument('--secret', required=True, help='HMAC Secret')
    parser.add_argument('--method', default='POST', help='HTTP Method')
    parser.add_argument('--path', required=True, help='URL Path (e.g. /api/v1/payments/intents)')
    parser.add_argument('--body', default='{}', help='JSON Body string')

    args = parser.parse_args()
    
    headers = generate_headers(args.apikey, args.secret, args.method, args.path, args.body)
    
    print("Generated Headers:")
    for k, v in headers.items():
        print(f'{k}: {v}')
    
    # Also print a curl command for convenience
    escaped_body = args.body.replace('"', '\\"')
    print("\nCurl Command:")
    print(f'curl -X {args.method} "http://localhost:5201{args.path}" \\')
    for k, v in headers.items():
        print(f'  -H "{k}: {v}" \\')
    print(f'  -H "Content-Type: application/json" \\')
    print(f'  -d "{escaped_body}"')
