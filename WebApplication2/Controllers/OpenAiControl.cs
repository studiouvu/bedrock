using OpenAI.Chat;
namespace Bedrock.Controllers;

public class OpenAiControl
{
    private static ChatClient _client;
    private static ChatClient _clientO1Mini;

    public static void Initialize()
    {
        _client = new ChatClient(model: "o1-preview", apiKey: Environment.GetEnvironmentVariable("OPENAI_API_KEY"));
        _clientO1Mini = new ChatClient(model: "o1-mini", apiKey: Environment.GetEnvironmentVariable("OPENAI_API_KEY"));
    }

    public static async Task<string> GetChat(string text)
    {
        ChatCompletion completion = await _client.CompleteChatAsync(text);
        return completion.Content[0].Text;
    }
    
    public static async Task<string> GetChatMini(string text)
    {
        ChatCompletion completion = await _clientO1Mini.CompleteChatAsync(text);
        return completion.Content[0].Text;
    }
}
