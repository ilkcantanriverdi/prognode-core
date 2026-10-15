// Release signing for in-app updates (Ed25519). The private key stays offline with the release owner;
// PROGNODE Monitor only installs an update whose signature matches the public key built into it.
//   node installer/sign-release.cjs keygen <private-key.pem>          writes the private key, prints the public key
//   node installer/sign-release.cjs sign <setup.exe> <Monitor|Client> <version> <private-key.pem>   writes <setup.exe>.sig
const crypto = require('node:crypto');
const fs = require('node:fs');

// Must match Prognode.Licensing.ReleaseSignature.Message.
const message = (product, version, sha256) => Buffer.from(`PROGNODE-RELEASE-V1\n${product}\n${version}\n${sha256}`, 'utf8');

const [mode, ...args] = process.argv.slice(2);
if (mode === 'keygen' && args.length === 1) {
  if (fs.existsSync(args[0])) throw new Error(`${args[0]} exists; refusing to overwrite a release key.`);
  const { privateKey, publicKey } = crypto.generateKeyPairSync('ed25519');
  fs.writeFileSync(args[0], privateKey.export({ type: 'pkcs8', format: 'pem' }), { mode: 0o600 });
  // Raw 32-byte public key = last 32 bytes of the SPKI DER.
  console.log(publicKey.export({ type: 'spki', format: 'der' }).subarray(-32).toString('base64'));
} else if (mode === 'sign' && args.length === 4) {
  const [file, product, version, keyPath] = args;
  if (!['Monitor', 'Client'].includes(product)) throw new Error('Product must be Monitor or Client.');
  const sha256 = crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
  const signature = crypto.sign(null, message(product, version, sha256), fs.readFileSync(keyPath));
  fs.writeFileSync(`${file}.sig`, signature.toString('base64'));
  console.log(`Signed ${file} (${product} ${version}).`);
} else {
  console.error('Usage: keygen <private-key.pem> | sign <setup.exe> <Monitor|Client> <version> <private-key.pem>');
  process.exit(2);
}
