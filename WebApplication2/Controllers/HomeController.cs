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

        var cookie = HttpContext.Request.Cookies["deviceId"];
        var deviceId = cookie ?? "";

        var userId = await GetUserId(deviceId);

        var value = new BedrockContent()
        {
            Id = Guid.NewGuid().ToString(),
            Partition = "0",
            Text = model.Data,
            Project = userId, // 프로젝트 아이디 가져오기 "project-0"
            Done = false,
            Tick = DateTime.UtcNow.Ticks,
        };

        await AwsKey.Context.SaveAsync(value);

        return await ReceiveContent();
    }

    public async Task<bool> FirstSetting(string userId)
    {
        // 프로젝트 아이디 가져오기 "project-0"
        await WriteContent(userId, "안녕하세요🥳 새로 오신 것을 환영합니다!");
        await WriteContent(userId, "Bedrock은 **강력한** Todo 앱입니다.  \n- **종단간 암호화**로 완전한 보안  \n*(당신 외에 누구도 이 글을 읽을 수 없습니다)*  \n- **MarkDown** 문법 지원  \n- **완전한 동기화** *웹 , 안드로이드 , 아이폰 어디서든 사용하세요*  \n- **오픈 소스** *(우리는 죽지 않습니다!)*");
        await WriteContent(userId, "오늘의 할 일");
        await WriteContent(userId, "물고기 밥 주기");
        await WriteContent(userId, "로그인하기");
        return true;
    }

    public async Task<bool> WriteContent(string projectId, string contentText)
    {
        var value = new BedrockContent()
        {
            Id = Guid.NewGuid().ToString(),
            Partition = "0",
            Text = contentText,
            Project = projectId,
            Done = false,
            Tick = DateTime.UtcNow.Ticks,
        };

        await AwsKey.Context.SaveAsync(value);

        return true;
    }

    [HttpPost]
    public async Task<IActionResult> ClickDone([FromBody] KeyInputModel model)
    {
        var id = model.Data;

        var content = await AwsKey.Context.LoadAsync<BedrockContent>("0", id);

        if (content == null)
            return NotFound("해당 아이템을 찾을 수 없습니다.");

        content.Done = true;
        content.DoneTick = DateTime.UtcNow.Ticks;

        await AwsKey.Context.SaveAsync(content);

        return await ReceiveContent();
    }

    [HttpPost]
    public async Task<IActionResult> ReceiveContent()
    {
        var cookie = HttpContext.Request.Cookies["deviceId"];
        var deviceId = cookie ?? "";
        var isFrist = false;

        if (string.IsNullOrEmpty(deviceId))
        {
            deviceId = Guid.NewGuid().ToString();
            HttpContext.Response.Cookies.Append("deviceId", deviceId);
            isFrist = true;
        }

        var userId = await GetUserId(deviceId);

        if (isFrist)
        {
            await FirstSetting(userId);
        }

        // 프로젝트 아이디 가져오기 "project-0"

        var conditions = new List<ScanCondition>
        {
            new("Project", ScanOperator.Equal, userId),
            new("Done", ScanOperator.Equal, false),
        };

        var contents = await AwsKey.Context.ScanAsync<BedrockContent>(conditions).GetRemainingAsync();

        if (contents.Count == 0)
            return Content("");

        StringBuilder builder = new();

        foreach (var content in contents.OrderBy(content => content.Tick))
        {
            //todo! 최적화하기 , 클라에서 해당 정보 가지고 있도록
            var dateTime = DateTime.MinValue.AddTicks(content.Tick);
            var timeSpan = DateTime.Now - DateTime.UtcNow;
            var fixedDateTime = dateTime.Add(timeSpan);

            // <font color="#6c6c6c">
            // {(fixedDateTime.Date != DateTime.Now.Date ? $"{fixedDateTime:MM.dd.yy}" : $"{fixedDateTime:HH:mm}")}
            // </font>

            var contentText = Markdown.ToHtml(content.Text).Replace("<p>", "").Replace("</p>", "");

            var text = $"""
                        <div style="max-width: 100%;">
                            <div class="ob-box" style="width=100%; cursor: text; background-color:transparent;">
                                <div style="width:100%; height:100%; align-items: center;">
                                 <div style="width:100%; height:100%; display: flex;">
                                     <div onclick="ClickDone('{content.Id}')" style="cursor: pointer; min-width: 18px; max-width:18px; min-height: 18px; max-height: 18px; border: solid #cdd0d4;  border-width:1px; margin-top: 3px; margin-right: 10px; border-radius: 5px;"></div>
                                         <div contenteditable="true" style="width:100%; cursor: text; border: none; outline: none;">
                                         {contentText}
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

    public async Task<string> GetUserId(string id)
    {
        var conditions = new List<ScanCondition>
        {
            new("Id", ScanOperator.Equal, id)
        };

        var allDocs = await AwsKey.Context.ScanAsync<DeviceId>(conditions).GetRemainingAsync();

        if (allDocs.Count == 0)
        {
            var newUserId = Guid.NewGuid().ToString();

            var deviceId = new DeviceId()
            {
                Id = id,
                UserId = newUserId,
                Partition = "0"
            };

            await AwsKey.Context.SaveAsync(deviceId);

            return newUserId;
        }

        var result = allDocs.First();

        return result.UserId;
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
