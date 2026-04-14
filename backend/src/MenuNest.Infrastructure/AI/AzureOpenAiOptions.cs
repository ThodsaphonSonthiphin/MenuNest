namespace MenuNest.Infrastructure.AI;

public sealed class GeminiOptions
{
    public const string SectionName = "Gemini";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gemini-2.0-flash";
}

// Keep AzureSpeechOptions as-is (Speech is still Azure)
public sealed class AzureSpeechOptions
{
    public const string SectionName = "AzureSpeech";
    public string Region { get; set; } = string.Empty;
    public string SubscriptionKey { get; set; } = string.Empty;
}
