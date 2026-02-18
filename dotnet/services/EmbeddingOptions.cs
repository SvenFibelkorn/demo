namespace dotnet.services;

public sealed class EmbeddingOptions
{
    public const string SectionName = "Embeddings";

    public string Provider { get; set; } = "ollama";

    public OllamaOptions Ollama { get; set; } = new();

    public LocalModelOptions Local { get; set; } = new();

    public int VectorSize { get; set; } = 768;
}

public sealed class OllamaOptions
{
    public string Endpoint { get; set; } = "http://localhost:11434";

    public string Model { get; set; } = "bge-base-en-v1.5";

    public int TimeoutSeconds { get; set; } = 60;
}

public sealed class LocalModelOptions
{
    public string ModelPath { get; set; } = "../models/bge-base-en-v1.5";

    public string OnnxModelPath { get; set; } = "onnx/model.onnx";

    public string TokenizerPath { get; set; } = "vocab.txt";

    public bool Lowercase { get; set; } = true;

    public int MaxLength { get; set; } = 512;
}
