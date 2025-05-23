﻿using OpenAI.Chat;
namespace Bedrock.Controllers;

public class OpenAiControl
{
    private static ChatClient _client;

    public static void Initialize()
    {
        _client = new ChatClient(model: "o1", apiKey: Environment.GetEnvironmentVariable("OPENAI_API_KEY"));
    }

    public static async Task<string> GetChat(string text)
    {
        return string.Empty;
        ChatCompletion completion = await _client.CompleteChatAsync(text);
        return completion.Content[0].Text;
    }
}
