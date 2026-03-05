#!/usr/bin/env node
const crypto = require("crypto");
function arg(name){ const i = process.argv.indexOf(`--${name}`); return i===-1?null:process.argv[i+1]; }
const url=arg("url"), clientId=arg("clientId"), secret=arg("secret"), body=arg("body")||"{}";
if(!url||!clientId||!secret){
  console.error("Usage: node scripts/sign-request.js --url <url> --clientId <id> --secret <secret> --body '<json>'");
  process.exit(1);
}
const timestamp=Math.floor(Date.now()/1000).toString();
const nonce=crypto.randomBytes(12).toString("hex");
const payload=`${timestamp}.${nonce}.${body}`;
const sig=crypto.createHmac("sha256", secret).update(payload).digest("hex");
console.log("curl -s -X POST " + JSON.stringify(url) + " \\");
console.log("  -H 'Content-Type: application/json' \\");
console.log("  -H 'X-Client-Id: " + clientId + "' \\");
console.log("  -H 'X-Timestamp: " + timestamp + "' \\");
console.log("  -H 'X-Nonce: " + nonce + "' \\");
console.log("  -H 'X-Signature: " + sig + "' \\");
console.log("  -H 'Idempotency-Key: " + crypto.randomUUID() + "' \\");
console.log("  -d " + JSON.stringify(body));
