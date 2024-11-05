using System.Diagnostics;
using System.Text;
using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Bedrock.Models;
using Markdig;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Bedrock.Views;

namespace Bedrock.Controllers;

public class KeyInputModel
{
    public string Data { get; set; }
}

public class BedrockContent
{
    public string Partition;
    public string Project;
    public string Id;
    public string Text;
    public bool Done;
    public long Tick;
    public long DoneTick;
    public long DoneIp;
}

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly IHubContext<MatchHub> _hubContext;

    private readonly StringBuilder _stringBuilder = new();

    public HomeController(ILogger<HomeController> logger, IHubContext<MatchHub> hubContext)
    {
        _logger = logger;
        _hubContext = hubContext;
    }

    public IActionResult Index()
    {
        return View();
    }

    public IActionResult Bedrock()
    {
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> ReceiveText([FromBody] KeyInputModel model)
    {
        if (string.IsNullOrEmpty(model.Data))
            return await ReceiveContent();
        
        var value = new BedrockContent()
        {
            Id = Guid.NewGuid().ToString(),
            Partition = "0",
            Text = model.Data,
            Project = "project-0",
            Done = false,
            Tick = DateTime.UtcNow.Ticks,
        };

        await AwsKey.Context.SaveAsync(value);

        return await ReceiveContent();
    }

    [HttpPost]
    public async Task<IActionResult> ClickDone([FromBody] KeyInputModel model)
    {
        var id = model.Data;
        
        // id로 아이템 로드
        var content = await context.LoadAsync<BedrockContent>(id);

        if (content == null)
            return NotFound("해당 아이템을 찾을 수 없습니다.");

        // 아이템 수정
        content.Text = newText;

        // 수정된 아이템 저장
        await context.SaveAsync(content);

        var updateItemRequest = new UpdateItemRequest
        {
            TableName = "BedrockContent",
            Key = new Dictionary<string, AttributeValue>
            {
                {
                    "Partition", new AttributeValue
                    {
                        S = "0"
                    }
                },
                {
                    "Id", new AttributeValue
                    {
                        S = id
                    }
                },
            },
            UpdateExpression = "SET #done = :newDoneValue",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                {
                    "#done", "Done"
                }
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                {
                    ":newDoneValue", new AttributeValue
                    {
                        BOOL = true
                    }
                }
            }
        };

        await AwsKey.Client.UpdateItemAsync(updateItemRequest);

        return await ReceiveContent();
    }

    [HttpPost]
    public async Task<IActionResult> ReceiveContent()
    {
        var conditions = new List<ScanCondition>
        {
            new("Project", ScanOperator.Equal, "project-0"),
            new("Done", ScanOperator.Equal, false),
        };

        var contents = await AwsKey.Context.ScanAsync<BedrockContent>(conditions).GetRemainingAsync();

        if (contents.Count == 0)
            return Content("");

        StringBuilder builder = new();

        foreach (var content in contents.OrderBy(content => content.Tick))
        {
            //todo! 최적화하기 , 클라에서 해당 정보 가지고 있도록
            var text = $"""
                        <div style="max-width: 100%;">
                            <div class="ob-box" style="width=100%; cursor: text; background-color:transparent;">
                                <div style="width:100%; height:100%; align-items: center;">
                                 <div style="width:100%; height:100%; display: flex;">
                                     <div onclick="ClickDone('{content.Id}')" style="cursor: pointer; min-width: 18px; max-width:18px; min-height: 18px; max-height: 18px; border: solid #cdd0d4;  border-width:1px; margin-top: 3px; margin-right: 10px; border-radius: 5px;"></div>
                                         <div contenteditable="true" style="cursor: text; border: none; outline: none;">
                                         {content.Text}
                                        <br>
                                        {DateTime.MinValue.AddTicks(content.Tick):MM.dd.yy HH:mm:ss}
                                         </div>
                                     </div>
                                </div>
                            </div>
                        </div>
                        """;

            builder.Append(text);
        }

        return Content(builder.ToString(), "text/html");
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
        });
    }
}
