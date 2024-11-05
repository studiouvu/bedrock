using Markdig;
namespace WebApplication2.Views;

public static class ASDF
{
    public static string GetASDF(string path)
    {
        var di = Environment.CurrentDirectory;

        var text = File.ReadAllText(di + "/wwwroot/" + path);
        var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
        var result = Markdown.ToHtml(text, pipeline);
        return result;
    }

    public static IOrderedEnumerable<FileInfo> GetFiles(string path)
    {
        var di = Environment.CurrentDirectory;

        var result = Directory.GetFiles(di + path)
            .Select(text => new FileInfo(text))
            .OrderByDescending(info => info.LastWriteTime);

        return result;
    }
}
