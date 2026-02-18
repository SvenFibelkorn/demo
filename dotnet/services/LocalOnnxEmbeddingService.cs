using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace dotnet.services;

public sealed class LocalOnnxEmbeddingService : IEmbeddingGenerator<string, Embedding<float>>, IDisposable
{
    private readonly EmbeddingOptions _options;
    private readonly InferenceSession? _session;
    private readonly BertTokenizer? _tokenizer;
    private readonly IReadOnlyDictionary<string, object?> _metadata;
    private readonly int _maxLength;
    private readonly Exception? _initializationException;

    public LocalOnnxEmbeddingService(
        IOptions<EmbeddingOptions> options,
        IHostEnvironment environment)
    {
        _options = options.Value;
        _maxLength = _options.Local.MaxLength;

        var modelRoot = ResolveModelRoot(environment, _options.Local.ModelPath);
        var onnxPath = Path.Combine(modelRoot, _options.Local.OnnxModelPath);
        var tokenizerPath = Path.Combine(modelRoot, _options.Local.TokenizerPath);

        if (!File.Exists(onnxPath))
        {
            throw new FileNotFoundException("ONNX model file was not found.", onnxPath);
        }

        if (!File.Exists(tokenizerPath))
        {
            throw new FileNotFoundException("Tokenizer vocab file was not found.", tokenizerPath);
        }

        try
        {
            _session = new InferenceSession(onnxPath);
            _tokenizer = BertTokenizer.Create(tokenizerPath, new BertOptions
            {
                LowerCaseBeforeTokenization = _options.Local.Lowercase
            });
        }
        catch (Exception ex)
        {
            _initializationException = ex;
        }

        _metadata = new Dictionary<string, object?>
        {
            ["modelRoot"] = modelRoot,
            ["onnxPath"] = onnxPath,
            ["initialized"] = _initializationException is null,
            ["initializationError"] = _initializationException?.Message
        };
    }

    public string ModelId => _options.Local.ModelPath;

    public IReadOnlyDictionary<string, object?> Metadata => _metadata;

    public object? GetService(Type serviceType, object? serviceKey) => null;

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (_initializationException is not null)
        {
            throw new InvalidOperationException(
                "Local ONNX embedding service is not available in this runtime environment.",
                _initializationException);
        }

        var inputs = values as IList<string> ?? values.ToList();
        if (inputs.Count == 0)
        {
            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(
                Array.Empty<Embedding<float>>()));
        }

        var results = new List<Embedding<float>>(inputs.Count);

        foreach (var text in inputs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var embedding = GenerateEmbedding(text);
            results.Add(new Embedding<float>(embedding));
        }

        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(results));
    }

    public void Dispose()
    {
        _session?.Dispose();
    }

    private float[] GenerateEmbedding(string text)
    {
        var session = _session
            ?? throw new InvalidOperationException("Local ONNX embedding service is not initialized.");
        var tokenizer = _tokenizer
            ?? throw new InvalidOperationException("Local ONNX embedding service is not initialized.");

        var inputIds = tokenizer.EncodeToIds(
            text,
            addSpecialTokens: true,
            considerPreTokenization: true,
            considerNormalization: true);

        var attentionMaskValues = CreateFilledArray(inputIds.Count, 1);
        var tokenTypeValues = CreateFilledArray(inputIds.Count, 0);

        var inputIdsTensor = BuildInputTensor(inputIds, _maxLength, tokenizer.PaddingTokenId);
        var attentionMask = BuildInputTensor(attentionMaskValues, _maxLength, 0);
        var tokenTypeIds = BuildInputTensor(tokenTypeValues, _maxLength, 0);

        var inputs = new List<NamedOnnxValue>();
        AddInput(session, inputs, "input_ids", inputIdsTensor.tensor, required: true);
        AddInput(session, inputs, "attention_mask", attentionMask.tensor, required: false);
        AddInput(session, inputs, "token_type_ids", tokenTypeIds.tensor, required: false);

        using var results = session.Run(inputs);
        var outputTensor = results.First().AsTensor<float>();

        return ExtractEmbedding(outputTensor, attentionMask.data);
    }

    private static (DenseTensor<long> tensor, long[] data) BuildInputTensor(
        IReadOnlyList<int> values,
        int maxLength,
        int padValue,
        int defaultValue = 0)
    {
        var targetLength = maxLength > 0 ? maxLength : values.Count;
        if (targetLength <= 0)
        {
            targetLength = 1;
        }

        var data = new long[targetLength];
        var copyLength = Math.Min(values.Count, targetLength);

        if (defaultValue != 0)
        {
            for (var i = 0; i < targetLength; i++)
            {
                data[i] = defaultValue;
            }
        }

        for (var i = 0; i < copyLength; i++)
        {
            data[i] = values[i];
        }

        if (copyLength < targetLength && padValue != 0)
        {
            for (var i = copyLength; i < targetLength; i++)
            {
                data[i] = padValue;
            }
        }

        var tensor = new DenseTensor<long>(data, new[] { 1, targetLength });
        return (tensor, data);
    }

    private static float[] ExtractEmbedding(Tensor<float> output, IReadOnlyList<long> attentionMask)
    {
        if (output.Dimensions.Length == 2)
        {
            return output.ToArray();
        }

        if (output.Dimensions.Length != 3)
        {
            throw new InvalidOperationException("Unexpected embedding output shape.");
        }

        var sequenceLength = output.Dimensions[1];
        var hiddenSize = output.Dimensions[2];
        var pooled = new float[hiddenSize];
        var tokenCount = 0f;

        for (var tokenIndex = 0; tokenIndex < sequenceLength; tokenIndex++)
        {
            var mask = tokenIndex < attentionMask.Count ? attentionMask[tokenIndex] : 1;
            if (mask == 0)
            {
                continue;
            }

            tokenCount += 1f;

            for (var featureIndex = 0; featureIndex < hiddenSize; featureIndex++)
            {
                pooled[featureIndex] += output[0, tokenIndex, featureIndex];
            }
        }

        if (tokenCount <= 0)
        {
            return pooled;
        }

        for (var featureIndex = 0; featureIndex < hiddenSize; featureIndex++)
        {
            pooled[featureIndex] /= tokenCount;
        }

        return pooled;
    }

    private static void AddInput(
        InferenceSession session,
        ICollection<NamedOnnxValue> inputs,
        string name,
        DenseTensor<long> tensor,
        bool required)
    {
        if (session.InputMetadata.ContainsKey(name))
        {
            inputs.Add(NamedOnnxValue.CreateFromTensor(name, tensor));
            return;
        }

        if (required)
        {
            throw new InvalidOperationException($"Required input '{name}' was not found in the ONNX model.");
        }
    }

    private static string ResolveModelRoot(IHostEnvironment environment, string modelPath)
    {
        if (Path.IsPathRooted(modelPath))
        {
            return modelPath;
        }

        var contentRoot = environment.ContentRootPath;
        return Path.GetFullPath(Path.Combine(contentRoot, modelPath));
    }

    private static int[] CreateFilledArray(int length, int value)
    {
        if (length <= 0)
        {
            return Array.Empty<int>();
        }

        var result = new int[length];
        if (value != 0)
        {
            for (var i = 0; i < length; i++)
            {
                result[i] = value;
            }
        }

        return result;
    }
}
