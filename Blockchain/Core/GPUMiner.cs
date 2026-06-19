using System;
using System.Linq;
using System.Text;
using System.Threading;
using Blockchain.Models;
using ILGPU;
using ILGPU.Runtime;

namespace Blockchain.Core
{
    public class GPUMiner
    {
        public Block Mine(Block block, CancellationToken cancellationToken)
        {
            try
            {
                return MineOnGPU(block, cancellationToken);
            }
            catch
            {
                var cpuMiner = new Miner();
                return cpuMiner.Mine(block);
            }
        }

        private Block MineOnGPU(Block block, CancellationToken cancellationToken)
        {
            var prefixBytes = Encoding.UTF8.GetBytes($"{block.Index}{block.PreviousHash}{block.Timestamp}");
            var txData = string.Join("|", block.Transactions.Select(t => t.Id));
            var suffixBytes = Encoding.UTF8.GetBytes($"{block.Difficulty}{txData}");

            using var context = Context.CreateDefault();
            using var accelerator = context.GetPreferredDevice(preferCPU: false).CreateAccelerator(context);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D,
                ArrayView<byte>,
                ArrayView<byte>,
                int,
                long,
                ArrayView<long>>(MineKernel);

            using var prefixBuffer = accelerator.Allocate1D(prefixBytes);
            using var suffixBuffer = accelerator.Allocate1D(suffixBytes);
            using var resultBuffer = accelerator.Allocate1D<long>(1);

            long startNonce = 0;
            const int batchSize = 1048576;
            long[] resultHolder = new long[1] { -1 };

            while (!cancellationToken.IsCancellationRequested)
            {
                resultBuffer.CopyFromCPU(resultHolder);

                kernel(batchSize, prefixBuffer.View, suffixBuffer.View, block.Difficulty, startNonce, resultBuffer.View);
                accelerator.Synchronize();

                var results = resultBuffer.GetAsArray1D();
                if (results[0] != -1)
                {
                    var localBlock = block.Clone();
                    localBlock.Nonce = results[0];
                    localBlock.Hash = Crypto.BlockHasher.Calculate(localBlock);
                    return localBlock;
                }

                startNonce += batchSize;
            }

            return block;
        }

        private static void MineKernel(
            Index1D index,
            ArrayView<byte> prefix,
            ArrayView<byte> suffix,
            int difficulty,
            long startNonce,
            ArrayView<long> resultNonce)
        {
            long nonce = startNonce + index;
            var buffer = new byte[1024];

            int offset = 0;
            for (int i = 0; i < prefix.Length; i++)
            {
                buffer[offset++] = prefix[i];
            }

            int nonceLen = WriteLongToBuffer(buffer, offset, nonce);
            offset += nonceLen;

            for (int i = 0; i < suffix.Length; i++)
            {
                buffer[offset++] = suffix[i];
            }

            SHA256State hash = HashData(buffer, offset);

            if (SatisfiesDifficulty(ref hash, difficulty))
            {
                resultNonce[0] = nonce;
            }
        }

        private static int WriteLongToBuffer(byte[] buffer, int offset, long value)
        {
            if (value == 0)
            {
                buffer[offset] = (byte)'0';
                return 1;
            }
            int length = 0;
            long temp = value;
            while (temp > 0)
            {
                length++;
                temp /= 10;
            }
            temp = value;
            for (int i = length - 1; i >= 0; i--)
            {
                buffer[offset + i] = (byte)('0' + (temp % 10));
                temp /= 10;
            }
            return length;
        }

        private static bool SatisfiesDifficulty(ref SHA256State hash, int difficulty)
        {
            int bytesToCheck = difficulty / 2;
            for (int i = 0; i < bytesToCheck; i++)
            {
                if (GetHashByte(ref hash, i) != 0)
                    return false;
            }
            if (difficulty % 2 != 0)
            {
                if ((GetHashByte(ref hash, bytesToCheck) & 0xF0) != 0)
                    return false;
            }
            return true;
        }

        private static byte GetHashByte(ref SHA256State hash, int byteIndex)
        {
            int wordIndex = byteIndex / 4;
            int offset = byteIndex % 4;
            uint word = 0;
            if (wordIndex == 0) word = hash.H0;
            else if (wordIndex == 1) word = hash.H1;
            else if (wordIndex == 2) word = hash.H2;
            else if (wordIndex == 3) word = hash.H3;
            else if (wordIndex == 4) word = hash.H4;
            else if (wordIndex == 5) word = hash.H5;
            else if (wordIndex == 6) word = hash.H6;
            else if (wordIndex == 7) word = hash.H7;

            if (offset == 0) return (byte)((word >> 24) & 0xFF);
            if (offset == 1) return (byte)((word >> 16) & 0xFF);
            if (offset == 2) return (byte)((word >> 8) & 0xFF);
            return (byte)(word & 0xFF);
        }

        private static SHA256State HashData(byte[] data, int length)
        {
            SHA256State state = new SHA256State();
            state.Init();

            ulong bitLength = (ulong)length * 8;
            data[length] = 0x80;

            int paddedLength = length + 1;
            while (paddedLength % 64 != 56)
            {
                data[paddedLength] = 0;
                paddedLength++;
            }

            data[paddedLength + 0] = (byte)((bitLength >> 56) & 0xFF);
            data[paddedLength + 1] = (byte)((bitLength >> 48) & 0xFF);
            data[paddedLength + 2] = (byte)((bitLength >> 40) & 0xFF);
            data[paddedLength + 3] = (byte)((bitLength >> 32) & 0xFF);
            data[paddedLength + 4] = (byte)((bitLength >> 24) & 0xFF);
            data[paddedLength + 5] = (byte)((bitLength >> 16) & 0xFF);
            data[paddedLength + 6] = (byte)((bitLength >> 8) & 0xFF);
            data[paddedLength + 7] = (byte)(bitLength & 0xFF);
            paddedLength += 8;

            uint[] w = new uint[64];

            for (int blockOffset = 0; blockOffset < paddedLength; blockOffset += 64)
            {
                for (int t = 0; t < 16; t++)
                {
                    int baseIdx = blockOffset + t * 4;
                    w[t] = ((uint)data[baseIdx] << 24) |
                           ((uint)data[baseIdx + 1] << 16) |
                           ((uint)data[baseIdx + 2] << 8) |
                           (uint)data[baseIdx + 3];
                }

                for (int t = 16; t < 64; t++)
                {
                    w[t] = Sigma1(w[t - 2]) + w[t - 7] + Sigma0(w[t - 15]) + w[t - 16];
                }

                uint a = state.H0;
                uint b = state.H1;
                uint c = state.H2;
                uint d = state.H3;
                uint e = state.H4;
                uint f = state.H5;
                uint g = state.H6;
                uint h = state.H7;

                for (int t = 0; t < 64; t++)
                {
                    uint temp1 = h + Ep1(e) + Ch(e, f, g) + GetK(t) + w[t];
                    uint temp2 = Ep0(a) + Maj(a, b, c);
                    h = g;
                    g = f;
                    f = e;
                    e = d + temp1;
                    d = c;
                    c = b;
                    b = a;
                    a = temp1 + temp2;
                }

                state.H0 += a;
                state.H1 += b;
                state.H2 += c;
                state.H3 += d;
                state.H4 += e;
                state.H5 += f;
                state.H6 += g;
                state.H7 += h;
            }

            return state;
        }

        private static uint RotateRight(uint x, int n) => (x >> n) | (x << (32 - n));
        private static uint Sigma0(uint x) => RotateRight(x, 7) ^ RotateRight(x, 18) ^ (x >> 3);
        private static uint Sigma1(uint x) => RotateRight(x, 17) ^ RotateRight(x, 19) ^ (x >> 10);
        private static uint Ep0(uint x) => RotateRight(x, 2) ^ RotateRight(x, 13) ^ RotateRight(x, 22);
        private static uint Ep1(uint x) => RotateRight(x, 6) ^ RotateRight(x, 11) ^ RotateRight(x, 25);
        private static uint Ch(uint x, uint y, uint z) => (x & y) ^ (~x & z);
        private static uint Maj(uint x, uint y, uint z) => (x & y) ^ (x & z) ^ (y & z);

        private static uint GetK(int index)
        {
            if (index < 16)
            {
                if (index < 8)
                {
                    if (index == 0) return 0x428a2f98; if (index == 1) return 0x71374491; if (index == 2) return 0xb5c0fbcf; if (index == 3) return 0xe9b5dba5;
                    if (index == 4) return 0x3956c25b; if (index == 5) return 0x59f111f1; if (index == 6) return 0x923f82a4; return 0xab1c5ed5;
                }
                if (index == 8) return 0xd807aa98; if (index == 9) return 0x12835b01; if (index == 10) return 0x243185be; if (index == 11) return 0x550c7dc3;
                if (index == 12) return 0x72be5d74; if (index == 13) return 0x80deb1fe; if (index == 14) return 0x9bdc06a7; return 0xc19bf174;
            }
            if (index < 32)
            {
                if (index < 24)
                {
                    if (index == 16) return 0xe49b69c1; if (index == 17) return 0xefbe4786; if (index == 18) return 0x0fc19dc6; if (index == 19) return 0x240ca1cc;
                    if (index == 20) return 0x2de92c6f; if (index == 21) return 0x4a7484aa; if (index == 22) return 0x5cb0a9dc; return 0x76f988da;
                }
                if (index == 24) return 0x983e5152; if (index == 25) return 0xa831c66d; if (index == 26) return 0xb00327c8; if (index == 27) return 0xbf597fc7;
                if (index == 28) return 0xc6e00bf3; if (index == 29) return 0xd5a79147; if (index == 30) return 0x06ca6351; return 0x14292967;
            }
            if (index < 48)
            {
                if (index < 40)
                {
                    if (index == 32) return 0x27b70a85; if (index == 33) return 0x2e1b2138; if (index == 34) return 0x4d2c6dfc; if (index == 35) return 0x53380d13;
                    if (index == 36) return 0x650a7354; if (index == 37) return 0x766a0abb; if (index == 38) return 0x81c2c92e; return 0x92722c85;
                }
                if (index == 40) return 0xa2bfe8a1; if (index == 41) return 0xa81a664b; if (index == 42) return 0xc24b8b70; if (index == 43) return 0xc76c51a3;
                if (index == 44) return 0xd192e819; if (index == 45) return 0xd6990624; if (index == 46) return 0xf40e3585; return 0x106aa070;
            }
            if (index < 56)
            {
                if (index == 48) return 0x19a4c116; if (index == 49) return 0x1e376c08; if (index == 50) return 0x2748774c; if (index == 51) return 0x34b0bcb5;
                if (index == 52) return 0x391c0cb3; if (index == 53) return 0x4ed8aa4a; if (index == 54) return 0x5b9cca4f; return 0x682e6ff3;
            }
            if (index == 56) return 0x748f82ee; if (index == 57) return 0x78a5636f; if (index == 58) return 0x84c87814; if (index == 59) return 0x8cc70208;
            if (index == 60) return 0x90befffa; if (index == 61) return 0xa4506ceb; if (index == 62) return 0xbef9a3f7; return 0xc67178f2;
        }
    }

    public struct SHA256State
    {
        public uint H0;
        public uint H1;
        public uint H2;
        public uint H3;
        public uint H4;
        public uint H5;
        public uint H6;
        public uint H7;

        public void Init()
        {
            H0 = 0x6a09e667;
            H1 = 0xbb67ae85;
            H2 = 0x3c6ef372;
            H3 = 0xa54ff53a;
            H4 = 0x510e527f;
            H5 = 0x9b05688c;
            H6 = 0x1f83d9ab;
            H7 = 0x5be0cd19;
        }
    }
}
