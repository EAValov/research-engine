using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using ResearchEngine.Domain;

namespace ResearchEngine.IntegrationTests.Infrastructure;

/// <summary>
/// Test-only embedder optimized for *near-duplicate* detection.
/// 
/// Key behavior:
/// - Order-insensitive: token sorting makes sentence re-ordering map to the same representation.
/// - Punctuation-insensitive.
/// - Produces very high cosine similarity for minor rewrites and reorderings.
/// 
/// Why we need this:
/// In production, LearningIntelService uses GroupAssignSimilarityThreshold = 0.93.
/// A generic "semantic" fake embedder won't necessarily make paraphrases exceed 0.93.
/// This embedder ensures that *near-duplicate* texts exceed that threshold reliably,
/// so we can test the "dedupe/group" path deterministically.
/// </summary>
public sealed class NearDuplicateFakeEmbeddingModel : IEmbeddingModel
{
    public string ModelId => "near-duplicate-fake-embedding-1024";

    public Task<Embedding<float>> GenerateEmbeddingAsync(string input, CancellationToken cancellationToken = default)
        => Task.FromResult(new Embedding<float>(MakeVector(input)));

    public Task<IReadOnlyList<Embedding<float>>> GenerateEmbeddingsAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Embedding<float>>>(inputs.Select(i => new Embedding<float>(MakeVector(i))).ToList());

    private static readonly Regex NonWord = new(@"[^\p{L}\p{N}\s]+", RegexOptions.Compiled);

    private static float[] MakeVector(string input)
    {
        const int dim = 1024;

        var s = (input ?? string.Empty).ToLowerInvariant();
        s = NonWord.Replace(s, " ");

        var tokens = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Order-insensitive normalization: sentence reorderings become nearly identical.
        Array.Sort(tokens, StringComparer.Ordinal);

        var vec = new float[dim];

        foreach (var tok in tokens)
        {
            var h = Fnv1a32(tok);
            var idx = (int)(h % dim);
            vec[idx] += 1f;
        }

        // L2 normalize for cosine distance stability.
        var norm = 0.0;
        for (int i = 0; i < dim; i++)
            norm += vec[i] * vec[i];

        norm = Math.Sqrt(norm);
        if (norm > 0)
        {
            var inv = (float)(1.0 / norm);
            for (int i = 0; i < dim; i++)
                vec[i] *= inv;
        }

        return vec;
    }

    private static uint Fnv1a32(string s)
    {
        unchecked
        {
            uint h = 2166136261;
            for (int i = 0; i < s.Length; i++)
                h = (h ^ s[i]) * 16777619;
            return h;
        }
    }
}
