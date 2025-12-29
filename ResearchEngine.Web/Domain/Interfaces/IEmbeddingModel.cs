using Microsoft.Extensions.AI;

namespace ResearchEngine.Domain;

public interface IEmbeddingModel
{
    string ModelId { get; }

    Task<Embedding<float>> GenerateEmbeddingAsync(
        string input,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Embedding<float>>> GenerateEmbeddingsAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default);
}
