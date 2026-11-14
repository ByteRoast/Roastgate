import crypto from 'node:crypto';

// RFC 6238 (HMAC-SHA1, 30 s steps, 6 digits), written here independently of the server, as an authenticator app would compute it.
const base32Decode = (text: string): Buffer<ArrayBuffer> => {
  const alphabet: string = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';

  let bits: string = '';

  for (const character of text.replace(/[\s=-]/g, '').toUpperCase()) {
    bits += alphabet.indexOf(character).toString(2).padStart(5, '0');
  }

  const bytes: any[] = [];

  for (let index: number = 0; index + 8 <= bits.length; index += 8) {
    bytes.push(parseInt(bits.slice(index, index + 8), 2));
  }

  return Buffer.from(bytes);
};

export const totp = (secret: string, stepOffset = 0): string => {
  const counter: Buffer<ArrayBuffer> = Buffer.alloc(8);
  counter.writeBigUInt64BE(BigInt(Math.floor(Date.now() / 30000) + stepOffset));

  const hash: Uint8Array = crypto
    .createHmac('sha1', base32Decode(secret))
    .update(counter)
    .digest();

  const offset: number = hash[hash.length - 1] & 0x0f;
  const binary: number =
    ((hash[offset] & 0x7f) << 24) |
    (hash[offset + 1] << 16) |
    (hash[offset + 2] << 8) |
    hash[offset + 3];

  return String(binary % 1000000).padStart(6, '0');
};
