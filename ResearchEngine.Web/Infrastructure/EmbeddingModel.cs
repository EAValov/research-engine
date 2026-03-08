using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Embeddings;
using ResearchEngine.Configuration;
using ResearchEngine.Domain;

namespace ResearchEngine.Infrastructure;

public sealed class OpenAiEmbeddingModel : IEmbeddingModel
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;

    public string ModelId { get; }

    public OpenAiEmbeddingModel(IOptions<EmbeddingConfig> options)
    {
        var cfg = options.Value ?? throw new ArgumentNullException(nameof(options));

        ModelId = cfg.ModelId;

        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(cfg.Endpoint)
        };

        if (string.IsNullOrWhiteSpace(cfg.ApiKey))
            throw new InvalidOperationException("Missing required configuration: EmbeddingConfig:ApiKey");

        var embeddingClient = new EmbeddingClient(cfg.ModelId, new ApiKeyCredential(cfg.ApiKey), clientOptions);

        _embeddingGenerator = embeddingClient.AsIEmbeddingGenerator();
    }

    public async Task<IReadOnlyList<Embedding<float>>> GenerateEmbeddingsAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default)
    {
        if (inputs is null) throw new ArgumentNullException(nameof(inputs));

        var result = await _embeddingGenerator.GenerateAsync(
                inputs,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return result;
    }

    public async Task<Embedding<float>> GenerateEmbeddingAsync(
        string input,
        CancellationToken cancellationToken = default)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));

        var result = await _embeddingGenerator.GenerateAsync(
                [input],
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return result[0];
    }
}
