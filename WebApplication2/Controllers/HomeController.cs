using System.Diagnostics;
using System.Net.Mail;
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
public class BedrockProject
{
    public string Partition;
    public string Id;
    public string UserId;
    public long CreateTick;
    public long DoneTick;
}
public class BedrockDeviceId
{
    public string Id;
    public string UserId;
    public string Partition;
}
public class BedrockEmailId
{
    public string Id;
    public string UserId;
    public string Partition;
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

    public async Task<IActionResult> Bedrock()
    {
        var deviceId = GetDeviceId();

        var isFrist = false;

        if (string.IsNullOrEmpty(deviceId))
        {
            deviceId = Guid.NewGuid().ToString();
            HttpContext.Response.Cookies.Append("deviceId", deviceId);
            isFrist = true;
        }

        var userId = await GetUserId(deviceId);

        if (isFrist)
            await FirstSetting(userId);

        var emailId = await GetEmailId(userId);

        ViewBag.Login = string.IsNullOrEmpty(emailId) == false;

        return View();
    }

    [HttpPost]
    public async Task<string> ReceiveUserId()
    {
        var deviceId = GetDeviceId();
        var userId = await GetUserId(deviceId);
        var emailId = await GetEmailId(userId);
        return string.IsNullOrEmpty(emailId) ? userId : emailId;
    }

    public string GetDeviceId()
    {
        var cookie = HttpContext.Request.Cookies["deviceId"];
        return cookie ?? "";
    }

    public async Task<string> GetEmailId(string userId)
    {
        var conditions = new List<ScanCondition>
        {
            new("UserId", ScanOperator.Equal, userId)
        };

        var emailIds = await AwsKey.Context.ScanAsync<BedrockEmailId>(conditions).GetRemainingAsync();

        if (emailIds.Count == 0)
            return string.Empty;

        var emailId = emailIds.First();
        return emailId.Id;
    }

    public async Task<bool> SendMail(string email)
    {
        var to = email;
        var from = "\"Bedrock Team\" <app@studiouvu.com>";
        var message = new MailMessage(from, to);

        var randomCode = new Random();
        var code = randomCode.Next(1000, 9999);

        var mailbody = $"<div style='padding: 20px;'><img src=\"https://bedrock.es/images/bedrock.png\"/><p>아래의 코드를 입력해주세요.</p><h1>{code}</h1></div>";
        message.Subject = "Bedrock 연동 인증 코드";
        message.Body = mailbody;
        message.Headers.Add("Sender", "Test");
        message.BodyEncoding = Encoding.UTF8;
        message.IsBodyHtml = true;

        var client = new SmtpClient("smtp.gmail.com", 587); //Gmail smtp    
        var basicCredential1 = new System.Net.NetworkCredential("app@studiouvu.com", "uwzt qfez aquv qwhm");

        client.EnableSsl = true;
        client.UseDefaultCredentials = false;
        client.Credentials = basicCredential1;

        await client.SendMailAsync(message);

        var credentials = new BasicAWSCredentials(AwsKey.accessKey, AwsKey.secretKey);
        var awsClient = new AmazonDynamoDBClient(credentials, RegionEndpoint.APNortheast1);

        var context = new DynamoDBContext(awsClient);

        var emailCode = new EmailCode
        {
            Email = email,
            Code = code,
            DateTime = DateTime.UtcNow,
            Partition = "bedrock"
        };

        await context.SaveAsync(emailCode);

        return true;
    }

    [HttpPost]
    public async Task<string> ReceiveEmailId([FromBody] KeyInputModel model)
    {
        var emailId = model.Data;
        HttpContext.Response.Cookies.Append("emailId", emailId);
        await SendMail(emailId);
        
        var html = $"""
                    <h7>
                         {emailId}로 전송된
                         <br>
                         인증 코드를 입력해주세요.
                    </h7>
                    <div style="max-width: 100%; margin-top: 10px; display: flex; align-items: center;">
                        <div class="text-center" style="width: 100%">
                            <div class="input-box-holder" style="max-width: 200px; margin: 0 auto; background: #092c47; overflow-wrap: break-word;">
                                <div style="width:100%; height: 100%; margin-left: 10px; margin-right: 10px;">
                                    <input id="code-input" class="input-box" type="text" placeholder="CODE" onKeyDown="SendCode(event)"/>
                                </div>
                            </div>
                        </div>
                    </div>
                """;
        
        return html;
    }

    [HttpPost]
    public async Task<bool> ReceiveEmailCode([FromBody] KeyInputModel model)
    {
        var emailId = HttpContext.Request.Cookies["emailId"];
        var code = model.Data;
        
        var verify = await VerifyCode(emailId, int.Parse(code));

        if (verify == false)
            return false;
        
        var userId = await GetUserIdToEmail(emailId);

        var cookie = HttpContext.Request.Cookies["deviceId"];
        var deviceId = cookie ?? "";

        if (string.IsNullOrEmpty(userId))
        {
            var userIdToDevice = await GetUserId(deviceId);

            var value = new BedrockEmailId()
            {
                Id = emailId,
                UserId = userIdToDevice,
                Partition = "0",
            };

            await AwsKey.Context.SaveAsync(value);
        }
        else
        {
            var value = new BedrockDeviceId()
            {
                Id = deviceId,
                UserId = userId,
                Partition = "0",
            };

            await AwsKey.Context.SaveAsync(value);
        }

        return true;
    }

    public async Task<bool> VerifyCode(string email, int code)
    {
        var credentials = new BasicAWSCredentials(AwsKey.accessKey, AwsKey.secretKey);
        var client = new AmazonDynamoDBClient(credentials, RegionEndpoint.APNortheast1);

        var context = new DynamoDBContext(client);

        var conditions = new List<ScanCondition>
        {
            new("Partition", ScanOperator.Equal, "bedrock"),
            new("Email", ScanOperator.Equal, email)
        };

        var allDocs = await context.ScanAsync<EmailCode>(conditions).GetRemainingAsync();

        if (allDocs.Count == 0)
            return false;

        var result = allDocs.First();

        return result.Code == code;
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
        await WriteContent(userId, "Bedrock은 **강력한** Todo 앱입니다.  \n- **종단 간 암호화**로 완전한 보안  \n*(당신 외에 누구도 이 글을 읽을 수 없습니다)*  \n- **MarkDown** 문법 지원  \n- **완전한 동기화** *웹 , 안드로이드 , 아이폰 어디서든 사용하세요*  \n- **오픈 소스** *(우리는 절대로 죽지 않습니다!)*  \n  \n자세한 건 이 [소개 글](https://bedrock.es/home/about)을 읽어주세요");
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
        var deviceId = GetDeviceId();
        var userId = await GetUserId(deviceId);

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

    public async Task<string> GetUserId(string emailId)
    {
        var conditions = new List<ScanCondition>
        {
            new("Id", ScanOperator.Equal, emailId)
        };

        var allDocs = await AwsKey.Context.ScanAsync<BedrockDeviceId>(conditions).GetRemainingAsync();

        if (allDocs.Count == 0)
        {
            var newUserId = Guid.NewGuid().ToString();

            var deviceId = new BedrockDeviceId()
            {
                Id = emailId,
                UserId = newUserId,
                Partition = "0"
            };

            await AwsKey.Context.SaveAsync(deviceId);

            return newUserId;
        }

        var result = allDocs.First();

        return result.UserId;
    }

    public async Task<string> GetUserIdToEmail(string emailId)
    {
        var conditions = new List<ScanCondition>
        {
            new("Id", ScanOperator.Equal, emailId)
        };

        var allDocs = await AwsKey.Context.ScanAsync<BedrockEmailId>(conditions).GetRemainingAsync();

        if (allDocs.Count == 0)
        {
            return null;
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
