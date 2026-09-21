// Cryptographically secure drop-in replacement for Math.random(), backed by the Web Crypto API.
export function secureRandom(): number {
  const buf = new Uint32Array(1);
  crypto.getRandomValues(buf);
  return buf[0] / 0x100000000;
}
