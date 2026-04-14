namespace MenuNest.Infrastructure.AI;

public sealed class AzureOpenAiOptions
{
    public const string SectionName = "AzureOpenAi";
    public string Endpoint { get; set; } = string.Empty;
    public string DeploymentName { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
}

public sealed class AzureSpeechOptions
{
    public const string SectionName = "AzureSpeech";
    public string Region { get; set; } = string.Empty;
    public string SubscriptionKey { get; set; } = string.Empty;
}
