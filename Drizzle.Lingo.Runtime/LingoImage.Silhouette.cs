using System;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using Serilog;

namespace Drizzle.Lingo.Runtime
{
    public sealed partial class LingoImage
    {
        public LingoImage makesilhouette(LingoNumber invertedI)
        {
            if (Depth != 32 && Depth != 1)
                throw new InvalidOperationException("makeSilhouette only supports 32 bit images right now");

            var inverted = LingoGlobal.ToBool(invertedI);
            var output = new LingoImage(Width, Height, 1);

            if (Depth == 32)
            {
                if (Avx2.IsSupported)
                    MakeSilhouette32ImplAvx2(this, output, inverted);
                else
                    MakeSilhouette32ImplScalar(this, output, inverted);
            }
            else if (Depth == 1)
            {
                MakeSilhouette1Impl(this, output, inverted);
            }

            return output;
        }

        private static void MakeSilhouette32ImplScalar(LingoImage src, LingoImage dst, bool inverted)
        {
            var srcBuf = MemoryMarshal.Cast<byte, uint>(src.ImageBuffer);
            var dstBuf = MemoryMarshal.Cast<byte, uint>(dst.ImageBuffer);

            var xorMask = inverted ? 0u : 0xFF_FF_FF_FFu;

            for (var i = 0; i < srcBuf.Length; i += 32)
            {
                var accum = 0u;

                var b = 0;
                for (var j = i; j < srcBuf.Length && b < 32; j++, b++)
                {
                    var white = srcBuf[j] == uint.MaxValue;
                    var bit = white ? 1u : 0u;
                    accum |= bit << b;
                }

                dstBuf[i >> 5] = xorMask ^ accum;
            }
        }

        private static unsafe void MakeSilhouette32ImplAvx2(LingoImage src, LingoImage dst, bool inverted)
        {
            var srcBuf = MemoryMarshal.Cast<byte, int>(src.ImageBuffer);
            var dstBuf = dst.ImageBuffer.AsSpan();

            var xorMask = (byte)(inverted ? 0xFF : 0);
            var lengthVec = Vector256.Create(srcBuf.Length);

            fixed (int* srcBase = srcBuf)
            {
                var posMask = Vector256.Create(0, 1, 2, 3, 4, 5, 6, 7);
                for (var i = 0; i < srcBuf.Length; i += 8)
                {
                    var pos = Avx2.Add(posMask, Vector256.Create(i));
                    var gt = Avx2.CompareGreaterThan(lengthVec, pos);
                    // Mask load to prevent out of bounds read.
                    var pixels = Avx2.MaskLoad(srcBase + i, gt);

                    var whiteMask = Avx2.CompareEqual(pixels, Vector256<int>.AllBitsSet);
                    // Ideally we would use (byte) MoveMask here and then use BMI2 PEXT
                    // But uh, AMD fucked that one up (before zen 3 it was really slow).
                    // I'll just take the cost of moving between int<->float regs and use float MoveMask I guess.
                    var mask = Avx.MoveMask(whiteMask.AsSingle());

                    dstBuf[i >> 3] = (byte)(mask ^ xorMask);
                }
            }
        }

        private static void MakeSilhouette1Impl(LingoImage src, LingoImage dst, bool inverted)
        {
            if (!inverted)
            {
                // There is literally no reason to do this because the result is the same.
                Log.Warning("MakeSilhouette called on 1-bit image without inversion");
                src.ImageBuffer.AsSpan().CopyTo(dst.ImageBuffer);
                return;
            }

            var srcBuf = MemoryMarshal.Cast<byte, uint>(src.ImageBuffer);
            var dstBuf = MemoryMarshal.Cast<byte, uint>(dst.ImageBuffer);

            for (var i = 0; i < srcBuf.Length; i++)
            {
                dstBuf[i] = ~srcBuf[i];
            }
        }
    }
}
