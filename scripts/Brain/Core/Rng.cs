using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace OtakLalat.Brain.Core;

/// <summary>xoshiro128** — RNG cepat, deterministik per NPC (seed), aman dipakai per-thread.</summary>
public struct Rng
{
    uint s0, s1, s2, s3;

    public Rng(ulong seed)
    {
        ulong z = seed + 0x9E3779B97F4A7C15UL;
        s0 = (uint)Mix(ref z); s1 = (uint)Mix(ref z); s2 = (uint)Mix(ref z); s3 = (uint)Mix(ref z);
        if ((s0 | s1 | s2 | s3) == 0) s0 = 1;
    }

    static ulong Mix(ref ulong z)
    {
        ulong x = z += 0x9E3779B97F4A7C15UL;
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        return x ^ (x >> 31);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint NextUInt()
    {
        uint result = BitOperations.RotateLeft(s1 * 5, 7) * 9;
        uint t = s1 << 9;
        s2 ^= s0; s3 ^= s1; s1 ^= s2; s0 ^= s3; s2 ^= t;
        s3 = BitOperations.RotateLeft(s3, 11);
        return result;
    }

    /// <summary>[0, 1)</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float NextFloat() => (NextUInt() >> 8) * (1f / 16777216f);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int NextInt(int n) => (int)(((ulong)NextUInt() * (ulong)n) >> 32);

    /// <summary>Sampel Poisson (Knuth untuk lambda kecil, aproksimasi normal untuk besar).</summary>
    public int Poisson(float lambda)
    {
        if (lambda <= 0) return 0;
        if (lambda > 30)
        {
            float u1 = MathF.Max(NextFloat(), 1e-7f), u2 = NextFloat();
            float z = MathF.Sqrt(-2f * MathF.Log(u1)) * MathF.Cos(2f * MathF.PI * u2);
            return Math.Max(0, (int)MathF.Round(lambda + z * MathF.Sqrt(lambda)));
        }
        float l = MathF.Exp(-lambda), p = 1f;
        int k = 0;
        do { k++; p *= NextFloat(); } while (p > l);
        return k - 1;
    }
}
