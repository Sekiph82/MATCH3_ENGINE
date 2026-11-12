using System;
using System.Numerics;
using System.Security.Cryptography;

namespace Match3.Engine.Runtime.LiveOps
{
    /// <summary>
    /// ENG-M20-009: ECDSA over NIST P-256 with SHA-256 ("ES256"), managed code only.
    /// Why managed: Unity's Mono class libraries (editor and player, unityjit/unityaot profiles) leave
    /// System.Security.Cryptography.ECDsa unimplemented (ECDsa.Create throws NotImplementedException, probed on
    /// Unity 6000.6.5f1 MonoBleedingEdge), and managed RSA-2048 key generation there takes 18-35 s.
    /// Wire formats match WebCrypto / .NET defaults so any standard backend signer interoperates:
    /// public key = SEC1 uncompressed point (65 bytes: 0x04 || X || Y), signature = r || s (64 bytes, IEEE P1363).
    /// Verify handles public data only, so variable-time arithmetic is acceptable there. GenerateKey/Sign are
    /// internal (tests and offline tooling): they are NOT constant-time and must never back a production signer;
    /// production signing keys are owner-reserved and live outside this repository.
    /// </summary>
    public static class EcdsaP256
    {
        public const int PublicKeyLength = 65;
        public const int SignatureLength = 64;

        private static readonly BigInteger P = Hex("ffffffff00000001000000000000000000000000ffffffffffffffffffffffff");
        private static readonly BigInteger N = Hex("ffffffff00000000ffffffffffffffffbce6faada7179e84f3b9cac2fc632551");
        private static readonly BigInteger B = Hex("5ac635d8aa3a93e7b3ebbd55769886bc651d06b0cc53b0f63bce3c3e27d2604b");
        private static readonly BigInteger Gx = Hex("6b17d1f2e12c4247f8bce6e563a440f277037d812deb33a0f4a13945d898c296");
        private static readonly BigInteger Gy = Hex("4fe342e2fe1a7f9b8ee7eb4a7c0f9e162bce33576b315ececbb6406837bf51f5");

        private struct Jacobian
        {
            public BigInteger X, Y, Z; // Z == 0 is the point at infinity
        }

        private static readonly Jacobian Infinity = new Jacobian { X = BigInteger.One, Y = BigInteger.One, Z = BigInteger.Zero };

        // Fixed-base table for G: GTable[i][j] = j * 16^i * G (64 x 16 points, built once on first use).
        private static readonly Lazy<Jacobian[][]> GTable = new Lazy<Jacobian[][]>(() =>
        {
            var table = new Jacobian[64][];
            var b = new Jacobian { X = Gx, Y = Gy, Z = BigInteger.One };
            for (var i = 0; i < 64; i++)
            {
                var row = new Jacobian[16];
                row[0] = Infinity;
                for (var j = 1; j < 16; j++) row[j] = Add(row[j - 1], b);
                table[i] = row;
                for (var d = 0; d < 4; d++) b = Double(b);
            }
            return table;
        });

        /// <summary>True when the bytes are a valid uncompressed P-256 point (on the curve, coordinates &lt; p).</summary>
        public static bool IsValidPublicKey(byte[] publicKey) => TryDecodePoint(publicKey, out _, out _);

        /// <summary>Verifies an r||s signature over SHA-256(message). Never throws; any malformed input is false.</summary>
        public static bool Verify(byte[] publicKey, byte[] message, byte[] signature)
        {
            if (message == null || signature == null || signature.Length != SignatureLength) return false;
            if (!TryDecodePoint(publicKey, out var qx, out var qy)) return false;
            var r = FromBigEndian(signature, 0, 32);
            var s = FromBigEndian(signature, 32, 32);
            if (r.IsZero || r >= N || s.IsZero || s >= N) return false;
            var e = HashToInt(message);
            var w = BigInteger.ModPow(s, N - 2, N);
            var sum = Add(MultiplyG(e * w % N), Multiply(r * w % N, qx, qy));
            if (sum.Z.IsZero) return false;
            ToAffine(sum, out var x, out _);
            return x % N == r;
        }

        internal static void GenerateKey(out byte[] privateKey, out byte[] publicKey)
        {
            var d = RandomScalar();
            privateKey = ToBigEndian32(d);
            publicKey = PublicKeyOf(privateKey);
        }

        internal static byte[] PublicKeyOf(byte[] privateKey)
        {
            var d = PrivateScalar(privateKey);
            ToAffine(MultiplyG(d), out var x, out var y);
            var pk = new byte[PublicKeyLength];
            pk[0] = 4;
            Array.Copy(ToBigEndian32(x), 0, pk, 1, 32);
            Array.Copy(ToBigEndian32(y), 0, pk, 33, 32);
            return pk;
        }

        internal static byte[] Sign(byte[] privateKey, byte[] message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            var d = PrivateScalar(privateKey);
            while (true)
            {
                var sig = SignWithNonce(d, message, RandomScalar());
                if (sig != null) return sig;
            }
        }

        /// <summary>Deterministic-nonce signing for known-answer tests (RFC 6979 vectors). Null if r or s is 0.</summary>
        internal static byte[] SignWithNonce(byte[] privateKey, byte[] message, byte[] nonce) =>
            SignWithNonce(PrivateScalar(privateKey), message, FromBigEndian(nonce, 0, nonce.Length));

        private static byte[] SignWithNonce(BigInteger d, byte[] message, BigInteger k)
        {
            if (k.IsZero || k >= N) throw new ArgumentOutOfRangeException(nameof(k));
            ToAffine(MultiplyG(k), out var x, out _);
            var r = x % N;
            if (r.IsZero) return null;
            var s = BigInteger.ModPow(k, N - 2, N) * ((HashToInt(message) + r * d) % N) % N;
            if (s.IsZero) return null;
            var sig = new byte[SignatureLength];
            Array.Copy(ToBigEndian32(r), 0, sig, 0, 32);
            Array.Copy(ToBigEndian32(s), 0, sig, 32, 32);
            return sig;
        }

        private static BigInteger PrivateScalar(byte[] privateKey)
        {
            if (privateKey == null || privateKey.Length != 32) throw new ArgumentException("32-byte private scalar required.", nameof(privateKey));
            var d = FromBigEndian(privateKey, 0, 32);
            if (d.IsZero || d >= N) throw new ArgumentException("Private scalar out of range.", nameof(privateKey));
            return d;
        }

        private static BigInteger RandomScalar()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                while (true)
                {
                    rng.GetBytes(bytes);
                    var k = FromBigEndian(bytes, 0, 32);
                    if (!k.IsZero && k < N) return k;
                }
            }
        }

        private static bool TryDecodePoint(byte[] pk, out BigInteger x, out BigInteger y)
        {
            x = y = BigInteger.Zero;
            if (pk == null || pk.Length != PublicKeyLength || pk[0] != 4) return false;
            x = FromBigEndian(pk, 1, 32);
            y = FromBigEndian(pk, 33, 32);
            if (x >= P || y >= P) return false;
            // y^2 = x^3 - 3x + b (mod p); the infinity point has no SEC1 uncompressed encoding.
            return Mod(y * y - (x * x * x - 3 * x + B)).IsZero;
        }

        /// <summary>k * G for 0 &lt;= k &lt; 2^256: one table addition per 4-bit window, no doublings.</summary>
        private static Jacobian MultiplyG(BigInteger k)
        {
            var table = GTable.Value;
            var result = Infinity;
            for (var i = 0; i < 64 && !k.IsZero; i++)
            {
                result = Add(result, table[i][(int)(k & 15)]);
                k >>= 4;
            }
            return result;
        }

        // Left-to-right double-and-add in Jacobian coordinates (a = -3).
        private static Jacobian Multiply(BigInteger k, BigInteger x, BigInteger y)
        {
            var result = Infinity;
            var point = new Jacobian { X = x, Y = y, Z = BigInteger.One };
            for (var i = BitLength(k) - 1; i >= 0; i--)
            {
                result = Double(result);
                if (!(k >> i).IsEven) result = Add(result, point);
            }
            return result;
        }

        private static Jacobian Double(Jacobian p)
        {
            if (p.Z.IsZero || p.Y.IsZero) return Infinity;
            var delta = Mod(p.Z * p.Z);
            var gamma = Mod(p.Y * p.Y);
            var beta = Mod(p.X * gamma);
            var alpha = Mod(3 * (p.X - delta) * (p.X + delta));
            var x3 = Mod(alpha * alpha - 8 * beta);
            var z3 = Mod((p.Y + p.Z) * (p.Y + p.Z) - gamma - delta);
            var y3 = Mod(alpha * (4 * beta - x3) - 8 * gamma * gamma);
            return new Jacobian { X = x3, Y = y3, Z = z3 };
        }

        private static Jacobian Add(Jacobian p, Jacobian q)
        {
            if (p.Z.IsZero) return q;
            if (q.Z.IsZero) return p;
            var z1z1 = Mod(p.Z * p.Z);
            var z2z2 = Mod(q.Z * q.Z);
            var u1 = Mod(p.X * z2z2);
            var u2 = Mod(q.X * z1z1);
            var s1 = Mod(p.Y * q.Z * z2z2);
            var s2 = Mod(q.Y * p.Z * z1z1);
            if (u1 == u2)
                return s1 == s2 ? Double(p) : Infinity;
            var h = Mod(u2 - u1);
            var i = Mod(4 * h * h);
            var j = Mod(h * i);
            var r = Mod(2 * (s2 - s1));
            var v = Mod(u1 * i);
            var x3 = Mod(r * r - j - 2 * v);
            var y3 = Mod(r * (v - x3) - 2 * s1 * j);
            var z3 = Mod(((p.Z + q.Z) * (p.Z + q.Z) - z1z1 - z2z2) * h);
            return new Jacobian { X = x3, Y = y3, Z = z3 };
        }

        private static void ToAffine(Jacobian p, out BigInteger x, out BigInteger y)
        {
            var zInv = BigInteger.ModPow(p.Z, P - 2, P);
            var zInv2 = Mod(zInv * zInv);
            x = Mod(p.X * zInv2);
            y = Mod(p.Y * zInv2 * zInv);
        }

        private static BigInteger HashToInt(byte[] message)
        {
            using (var sha = SHA256.Create()) return FromBigEndian(sha.ComputeHash(message), 0, 32); // 256-bit hash = bitlen(n)
        }

        private static BigInteger Mod(BigInteger v)
        {
            var m = v % P;
            return m.Sign < 0 ? m + P : m;
        }

        private static int BitLength(BigInteger v)
        {
            var bits = 0;
            while (!v.IsZero) { v >>= 1; bits++; }
            return bits;
        }

        private static BigInteger FromBigEndian(byte[] src, int offset, int length)
        {
            var le = new byte[length + 1]; // trailing 0 keeps the value unsigned
            for (var i = 0; i < length; i++) le[i] = src[offset + length - 1 - i];
            return new BigInteger(le);
        }

        private static byte[] ToBigEndian32(BigInteger v)
        {
            var le = v.ToByteArray();
            var be = new byte[32];
            for (var i = 0; i < 32 && i < le.Length; i++) be[31 - i] = le[i];
            return be;
        }

        private static BigInteger Hex(string hex)
        {
            var bytes = new byte[hex.Length / 2];
            for (var i = 0; i < bytes.Length; i++) bytes[i] = Convert.ToByte(hex.Substring(2 * i, 2), 16);
            return FromBigEndian(bytes, 0, bytes.Length);
        }
    }
}
