using System.Text;
using Microsoft.Extensions.AI;
using Talkie.Shared;

namespace Talkie.Api.Infrastructure;

/// <summary>
/// A small, dependency-free embedding generator based on feature hashing of words and character trigrams.
/// It runs fully offline and produces L2-normalised vectors that are stored in SQL Server's native
/// <c>vector</c> column and compared with <c>VECTOR_DISTANCE('cosine', ...)</c>.
/// Swap it for any other <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/> (e.g. OpenAI or Ollama)
/// as long as it produces <see cref="TalkieConstants.EmbeddingDimensions"/> dimensions.
/// </summary>
public sealed class HashingEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private const int Dimensions = TalkieConstants.EmbeddingDimensions;

    public EmbeddingGeneratorMetadata Metadata { get; } = new("talkie-hashing", null, "hashing-v1", Dimensions);

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var result = new GeneratedEmbeddings<Embedding<float>>(values.Select(v => new Embedding<float>(Embed(v))));
        return Task.FromResult(result);
    }

    public static float[] Embed(string text)
    {
        var vector = new float[Dimensions];
        foreach (var word in Tokenize(text))
        {
            Add(vector, "w:" + word, 1.0f);
            var padded = $"#{word}#";
            for (var i = 0; i + 3 <= padded.Length; i++)
            {
                Add(vector, "t:" + padded.Substring(i, 3), 0.35f);
            }
        }

        var norm = MathF.Sqrt(vector.Sum(v => v * v));
        if (norm > 0)
        {
            for (var i = 0; i < vector.Length; i++) vector[i] /= norm;
        }
        else
        {
            vector[0] = 1f; // SQL Server cannot compute cosine distance with a zero vector.
        }
        return vector;
    }

    internal static IEnumerable<string> Tokenize(string text)
    {
        var sb = new StringBuilder();
        foreach (var ch in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) { sb.Append(ch); continue; }
            if (sb.Length > 0) { yield return Stem(sb.ToString()); sb.Clear(); }
        }
        if (sb.Length > 0) yield return Stem(sb.ToString());
    }

    private static string Stem(string word)
    {
        if (word.Length > 5 && word.EndsWith("ing")) return word[..^3];
        if (word.Length > 4 && word.EndsWith("ed")) return word[..^2];
        if (word.Length > 3 && word.EndsWith('s') && !word.EndsWith("ss")) return word[..^1];
        return word;
    }

    private static void Add(float[] vector, string feature, float weight)
    {
        var hash = Fnv1A(feature);
        var index = (int)(hash % Dimensions);
        var sign = ((hash >> 31) & 1) == 0 ? 1f : -1f;
        vector[index] += sign * weight;
    }

    private static uint Fnv1A(string s)
    {
        var hash = 2166136261u;
        foreach (var ch in s)
        {
            hash ^= ch;
            hash *= 16777619u;
        }
        return hash;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() { }
}
