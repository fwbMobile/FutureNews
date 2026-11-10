using System.Numerics;
using FutureNews.Core.Fetching;

namespace FutureNews.Core.Processing;

/// <summary>
/// 64-bit SimHash over word shingles. Two near-identical headlines land within a small
/// Hamming distance even when the wording differs slightly, which lets the tool suppress
/// the same story repeated by several outlets.
/// </summary>
public static class SimHash
{
    public static long Compute(string normalizedTitle)
    {
        if (string.IsNullOrWhiteSpace(normalizedTitle)) return 0;

        var tokens = normalizedTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return 0;

        var shingles = new List<string>();
        if (tokens.Length == 1)
        {
            shingles.Add(tokens[0]);
        }
        else
        {
            for (var i = 0; i + 2 < tokens.Length; i++) shingles.Add($"{tokens[i]}_{tokens[i + 1]}_{tokens[i + 2]}");
            if (shingles.Count == 0) shingles.AddRange(tokens);
        }

        var vector = new int[64];
        var counts = new Dictionary<string, int>();
        foreach (var shingle in shingles)
            counts[shingle] = counts.GetValueOrDefault(shingle) + 1;

        foreach (var (shingle, count) in counts)
        {
            var hash = Fnv1A(shingle);
            for (var bit = 0; bit < 64; bit++)
            {
                if (((hash >> bit) & 1) == 1) vector[bit] += count;
                else vector[bit] -= count;
            }
        }

        long result = 0;
        for (var bit = 0; bit < 64; bit++)
        {
            if (vector[bit] > 0) result |= 1L << bit;
        }
        return result;
    }

    public static int HammingDistance(long a, long b) => BitOperations.PopCount((ulong)(a ^ b));

    private static ulong Fnv1A(string value)
    {
        const ulong offset = 14695981039346656037;
        const ulong prime = 1099511628211;
        var hash = offset;
        foreach (var ch in value)
        {
            hash ^= ch;
            hash *= prime;
        }
        return hash;
    }
}
