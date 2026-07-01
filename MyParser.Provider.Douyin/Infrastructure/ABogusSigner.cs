using System.Security.Cryptography;
using System.Text;

namespace MyParser.Provider.Douyin.Infrastructure;

public static class ABogusSigner
{
    private const string ResultAlphabet = "Dkdpgh2ZmsQB80/MfvV36XI1R45-WUAlEixNLwoqYTOPuzKFjJnry79HbGcaStCe";
    private const string EndString = "cus";
    private const string Rc4Key = "y";

    private static readonly byte[] UaCode =
    [
        76, 98, 15, 131, 97, 245, 224, 133,
        122, 199, 241, 166, 79, 34, 90, 191,
        128, 126, 122, 98, 66, 11, 14, 40,
        49, 110, 110, 173, 67, 96, 138, 252,
    ];

    private static readonly byte[] BrowserCode = Encoding.UTF8.GetBytes("1920|1080|1920|1080|0|0|0|0|1920|1080|1920|1080|1920|1080|24|24|Win32");

    public static string Generate(string queryString, string userAgent)
    {
        var startTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var endTime = startTime + RandomNumberGenerator.GetInt32(4, 9);

        var bytes = new List<byte>(160);
        bytes.AddRange(GenerateString1());
        bytes.AddRange(GenerateString2(queryString, startTime, endTime));
        return EncodeResult(bytes.ToArray());
    }

    private static byte[] GenerateString1()
    {
        return List1().Concat(List2()).Concat(List3()).ToArray();
    }

    private static byte[] GenerateString2(string urlParams, long startTime, long endTime)
    {
        var payload = GenerateString2List(urlParams, startTime, endTime).ToList();
        var check = EndCheckNum(payload);
        payload.AddRange(BrowserCode);
        payload.Add(check);
        return Rc4(payload.ToArray(), Rc4Key);
    }

    private static byte[] GenerateString2List(string urlParams, long startTime, long endTime)
    {
        var paramsArray = DoubleSm3(urlParams + EndString);
        var methodArray = DoubleSm3("GET" + EndString);
        return List4(
            (byte)((endTime >> 24) & 0xff),
            paramsArray[21],
            UaCode[23],
            (byte)((endTime >> 16) & 0xff),
            paramsArray[22],
            UaCode[24],
            (byte)((endTime >> 8) & 0xff),
            (byte)(endTime & 0xff),
            (byte)((startTime >> 24) & 0xff),
            (byte)((startTime >> 16) & 0xff),
            (byte)((startTime >> 8) & 0xff),
            (byte)(startTime & 0xff),
            methodArray[21],
            methodArray[22],
            (byte)((endTime >> 32) & 0xff),
            (byte)((startTime >> 32) & 0xff),
            (byte)BrowserCode.Length);
    }

    private static byte[] List1() => RandomList(170, 85, 1, 2, 5, 45 & 170);

    private static byte[] List2() => RandomList(170, 85, 1, 0, 0, 0);

    private static byte[] List3() => RandomList(170, 85, 1, 0, 5, 0);

    private static byte[] RandomList(int b, int c, int d, int e, int f, int g)
    {
        var r = Random.Shared.NextDouble() * 10000;
        var v1 = ((int)r) & 255;
        var v2 = ((int)r) >> 8;
        return
        [
            (byte)((v1 & b) | d),
            (byte)((v1 & c) | e),
            (byte)((v2 & b) | f),
            (byte)((v2 & c) | g),
        ];
    }

    private static byte[] List4(byte a, byte b, byte c, byte d, byte e, byte f, byte g, byte h, byte i, byte j, byte k, byte m, byte n, byte o, byte p, byte q, byte r)
    {
        return
        [
            44, a, 0, 0, 0, 0, 24, b, n, 0, c, d, 0, 0, 0, 1,
            0, 239, e, o, f, g, 0, 0, 0, 0, h, 0, 0, 14, i, j,
            0, k, m, 3, p, 1, q, 1, r, 0, 0, 0,
        ];
    }

    private static byte EndCheckNum(IEnumerable<byte> bytes)
    {
        byte result = 0;
        foreach (var item in bytes)
        {
            result ^= item;
        }

        return result;
    }

    private static byte[] DoubleSm3(string value)
    {
        return Sm3.Hash(Sm3.Hash(Encoding.UTF8.GetBytes(value)));
    }

    private static byte[] Rc4(byte[] plaintext, string key)
    {
        var s = Enumerable.Range(0, 256).ToArray();
        var keyBytes = Encoding.ASCII.GetBytes(key);
        var j = 0;
        for (var i = 0; i < 256; i++)
        {
            j = (j + s[i] + keyBytes[i % keyBytes.Length]) & 255;
            (s[i], s[j]) = (s[j], s[i]);
        }

        var output = new byte[plaintext.Length];
        var x = 0;
        j = 0;
        for (var k = 0; k < plaintext.Length; k++)
        {
            x = (x + 1) & 255;
            j = (j + s[x]) & 255;
            (s[x], s[j]) = (s[j], s[x]);
            var t = (s[x] + s[j]) & 255;
            output[k] = (byte)(plaintext[k] ^ s[t]);
        }

        return output;
    }

    private static string EncodeResult(byte[] bytes)
    {
        var sb = new StringBuilder(((bytes.Length + 2) / 3) * 4);
        for (var i = 0; i < bytes.Length; i += 3)
        {
            var remaining = bytes.Length - i;
            var n = bytes[i] << 16;
            if (remaining > 1)
            {
                n |= bytes[i + 1] << 8;
            }

            if (remaining > 2)
            {
                n |= bytes[i + 2];
            }

            sb.Append(ResultAlphabet[(n & 0xfc0000) >> 18]);
            sb.Append(ResultAlphabet[(n & 0x03f000) >> 12]);
            sb.Append(remaining > 1 ? ResultAlphabet[(n & 0x000fc0) >> 6] : '=');
            sb.Append(remaining > 2 ? ResultAlphabet[n & 0x00003f] : '=');
        }

        return sb.ToString();
    }

    private static class Sm3
    {
        private static readonly uint[] Iv =
        [
            0x7380166f, 0x4914b2b9, 0x172442d7, 0xda8a0600,
            0xa96f30bc, 0x163138aa, 0xe38dee4d, 0xb0fb0e4e,
        ];

        public static byte[] Hash(byte[] input)
        {
            var padded = Pad(input);
            var v = Iv.ToArray();
            for (var offset = 0; offset < padded.Length; offset += 64)
            {
                Compress(v, padded.AsSpan(offset, 64));
            }

            var output = new byte[32];
            for (var i = 0; i < v.Length; i++)
            {
                WriteUInt32BigEndian(output.AsSpan(i * 4, 4), v[i]);
            }

            return output;
        }

        private static byte[] Pad(byte[] input)
        {
            var bitLength = (ulong)input.Length * 8;
            var paddingLength = 1 + ((56 - (input.Length + 1) % 64 + 64) % 64) + 8;
            var padded = new byte[input.Length + paddingLength];
            Buffer.BlockCopy(input, 0, padded, 0, input.Length);
            padded[input.Length] = 0x80;
            for (var i = 0; i < 8; i++)
            {
                padded[^(i + 1)] = (byte)(bitLength >> (8 * i));
            }

            return padded;
        }

        private static void Compress(uint[] v, ReadOnlySpan<byte> block)
        {
            Span<uint> w = stackalloc uint[68];
            Span<uint> w1 = stackalloc uint[64];
            for (var i = 0; i < 16; i++)
            {
                w[i] = ReadUInt32BigEndian(block.Slice(i * 4, 4));
            }

            for (var i = 16; i < 68; i++)
            {
                w[i] = P1(w[i - 16] ^ w[i - 9] ^ RotateLeft(w[i - 3], 15)) ^ RotateLeft(w[i - 13], 7) ^ w[i - 6];
            }

            for (var i = 0; i < 64; i++)
            {
                w1[i] = w[i] ^ w[i + 4];
            }

            var a = v[0];
            var b = v[1];
            var c = v[2];
            var d = v[3];
            var e = v[4];
            var f = v[5];
            var g = v[6];
            var h = v[7];

            for (var j = 0; j < 64; j++)
            {
                var tj = j < 16 ? 0x79cc4519u : 0x7a879d8au;
                var ss1 = RotateLeft(unchecked(RotateLeft(a, 12) + e + RotateLeft(tj, j)), 7);
                var ss2 = ss1 ^ RotateLeft(a, 12);
                var tt1 = unchecked(FF(a, b, c, j) + d + ss2 + w1[j]);
                var tt2 = unchecked(GG(e, f, g, j) + h + ss1 + w[j]);
                d = c;
                c = RotateLeft(b, 9);
                b = a;
                a = tt1;
                h = g;
                g = RotateLeft(f, 19);
                f = e;
                e = P0(tt2);
            }

            v[0] ^= a;
            v[1] ^= b;
            v[2] ^= c;
            v[3] ^= d;
            v[4] ^= e;
            v[5] ^= f;
            v[6] ^= g;
            v[7] ^= h;
        }

        private static uint FF(uint x, uint y, uint z, int j) => j < 16 ? x ^ y ^ z : (x & y) | (x & z) | (y & z);

        private static uint GG(uint x, uint y, uint z, int j) => j < 16 ? x ^ y ^ z : (x & y) | (~x & z);

        private static uint P0(uint x) => x ^ RotateLeft(x, 9) ^ RotateLeft(x, 17);

        private static uint P1(uint x) => x ^ RotateLeft(x, 15) ^ RotateLeft(x, 23);

        private static uint RotateLeft(uint value, int bits)
        {
            bits &= 31;
            return (value << bits) | (value >> (32 - bits));
        }

        private static uint ReadUInt32BigEndian(ReadOnlySpan<byte> bytes)
        {
            return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
        }

        private static void WriteUInt32BigEndian(Span<byte> bytes, uint value)
        {
            bytes[0] = (byte)(value >> 24);
            bytes[1] = (byte)(value >> 16);
            bytes[2] = (byte)(value >> 8);
            bytes[3] = (byte)value;
        }
    }
}
