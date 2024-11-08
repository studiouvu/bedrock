using System.Diagnostics;
using System.Net.Mail;
using System.Text;
using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.Runtime;
using Bedrock.Models;
using Markdig;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace Bedrock.Controllers;

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
            HttpContext.Response.Cookies.Append("version", "0.1");
            isFrist = true;
        }

        var userId = await GetUserId(deviceId);

        if (isFrist)
        {
            var firstProject = await FirstSetting(userId);

            var userSetting = await GetUserSetting(userId);
            userSetting.CurrentProject = firstProject;
            userSetting.ShowDate = true;
            await SaveUserSetting(userSetting);
        }

        var emailId = await GetEmailId(userId);

        ViewBag.Login = string.IsNullOrEmpty(emailId) == false;

        return View();
    }

    [HttpPost]
    public async Task<bool> ReceiveCreateProject()
    {
        var deviceId = GetDeviceId();
        var userId = await GetUserId(deviceId);
        var newProject = await CreateProject(userId);

        var userSetting = await GetUserSetting(userId);
        userSetting.CurrentProject = newProject.Id;

        await SaveUserSetting(userSetting);

        return true;
    }

    [HttpPost]
    public async Task<string> ReceiveText([FromBody] DataModel model)
    {
        if (string.IsNullOrEmpty(model.Data))
            return string.Empty;

        var deviceId = GetDeviceId();
        var userId = await GetUserId(deviceId);
        var userSetting = await GetUserSetting(userId);

        var content = await WriteContent(userSetting.CurrentProject, model.Data);

        var html = ContentToHtml(content, userSetting.ShowDate);

        return html;
    }

    [HttpPost]
    public async Task<bool> ReceiveChangeProject([FromBody] DataModel data)
    {
        var deviceId = GetDeviceId();
        var userId = await GetUserId(deviceId);
        var userSetting = await GetUserSetting(userId);

        var projectId = data.Data;
        userSetting.CurrentProject = projectId;

        await SaveUserSetting(userSetting);

        var project = await GetProject(projectId);
        project.LastOpenTick = DateTime.UtcNow.Ticks;
        await SaveProject(project);

        return true;
    }

    [HttpPost]
    public async Task<string> ReceiveUserId()
    {
        var deviceId = GetDeviceId();
        var userId = await GetUserId(deviceId);
        var emailId = await GetEmailId(userId);
        return string.IsNullOrEmpty(emailId) ? userId : emailId;
    }

    [HttpPost]
    public async Task<string> ReceiveCurrentProjectName()
    {
        var deviceId = GetDeviceId();
        var userId = await GetUserId(deviceId);
        var userSetting = await GetUserSetting(userId);

        var bedrockProject = await GetProject(userSetting.CurrentProject);

        return bedrockProject.Name;
    }

    [HttpPost]
    public async Task<bool> ReceiveSendProjectName([FromBody] DataModel model)
    {
        var deviceId = GetDeviceId();
        var userId = await GetUserId(deviceId);
        var userSetting = await GetUserSetting(userId);

        var bedrockProject = await GetProject(userSetting.CurrentProject);

        if (bedrockProject.Name == model.Data)
            return true;
        
        bedrockProject.Name = model.Data;
        await SaveProject(bedrockProject);

        return true;
    }

    [HttpPost]
    public async Task<string> ReceiveLastProjectList()
    {
        var deviceId = GetDeviceId();
        var userId = await GetUserId(deviceId);
        var projects = await ReceiveProjects(userId);

        var builder = new StringBuilder();

        foreach (var project in projects.OrderByDescending(project => project.LastOpenTick))
        {
            var text = $"""
                            <div
                            class="click-color unselectable"
                            onclick="ChangeProject('{project.Id}')"
                            style="cursor: pointer; height: 100%; background-color: #1f1f1f; padding: 6px 9px; border-radius: 10px; margin-right: 6px;">
                                  <div class="text-center">
                                      {project.Name}
                                  </div>
                            </div>
                        """;
            builder.Append(text);
        }

        return builder.ToString();
    }

    [HttpPost]
    public async Task<string> ReceiveProjectList()
    {
        var deviceId = GetDeviceId();
        var userId = await GetUserId(deviceId);
        var userSetting = await GetUserSetting(userId);
        var projects = await ReceiveProjects(userId);

        var builder = new StringBuilder();

        foreach (var project in projects.OrderByDescending(project => project.CreateTick))
        {
            var backgroundColor = project.Id == userSetting.CurrentProject ? "#1f1f1f" : "transparent";

            var text = $"""
                            <div
                                class="click-color unselectable"
                               onclick="ChangeProject('{project.Id}')"
                               style="width:95%; display: inline-block; cursor: pointer; background-color: {backgroundColor}; border-radius: 10px; padding: 4px 8px;">
                               {project.Name}
                            </div>
                            <br>
                        """;
            builder.Append(text);
        }

        return builder.ToString();
    }


    [HttpPost]
    public async Task<IActionResult> ClickDone([FromBody] DataModel model)
    {
        var id = model.Data;

        var content = await AwsKey.Context.LoadAsync<BedrockContent>("0", id);

        if (content == null)
            return NotFound("해당 아이템을 찾을 수 없습니다.");

        content.Done = true;
        content.DoneTick = DateTime.UtcNow.Ticks;

        await AwsKey.Context.SaveAsync(content);

        return await ReceiveFullContent();
    }

    [HttpPost]
    public async Task<IActionResult> ReceiveFullContent()
    {
        var deviceId = GetDeviceId();
        var userId = await GetUserId(deviceId);
        var userSetting = await GetUserSetting(userId);

        var project = userSetting.CurrentProject;

        var conditions = new List<ScanCondition>
        {
            new("Project", ScanOperator.Equal, project),
            new("Done", ScanOperator.Equal, false),
        };

        var contents = await AwsKey.Context.ScanAsync<BedrockContent>(conditions).GetRemainingAsync();

        if (contents.Count == 0)
            return Content("");

        StringBuilder builder = new();

        foreach (var content in contents.OrderBy(content => content.Tick))
        {
            var html = ContentToHtml(content, userSetting.ShowDate);
            builder.Append(html);
        }

        return Content(builder.ToString(), "text/html");
    }

    [HttpPost]
    public async Task<string> ReceiveEmailId([FromBody] DataModel model)
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
                        <div style="max-width: 200px; margin: 0 auto; margin-top: 10px; display: flex; align-items: center;">
                            <div class="text-center" >
                                <div class="input-box-holder" style="width: 100%;  background: #092c47; overflow-wrap: break-word;">
                                    <div style="width:100%; height: 100%; margin-left: 10px; margin-right: 10px;">
                                        <input id="code-input" class="input-box" type="text" placeholder="CODE" onKeyDown="SendCode(event)"/>
                                    </div>
                                </div>
                            </div>
                            <div
                                   class="click-color unselectable input-box-holder"
                                   onclick="SendCodeForce()"
                                   style="background: #092c47; cursor: pointer; min-width: 42px; min-height: 42px; display: flex; align-items: center;
                                   padding: 5px; margin-left: 5px; ">
                                   <div class="text-center" style="width: 100%; align-items: center;">
                                       →
                                   </div>
                               </div>
                        </div>
                    """;

        return html;
    }

    [HttpPost]
    public async Task<bool> ReceiveEmailCode([FromBody] DataModel model)
    {
        var emailId = HttpContext.Request.Cookies["emailId"];
        var code = model.Data;

        var verify = await VerifyCode(emailId, code);

        if (verify == false)
            return false;

        var userId = await GetUserIdToEmail(emailId);

        var deviceId = GetDeviceId();

        if (string.IsNullOrEmpty(userId))
        {
            var userIdByDevice = await GetUserId(deviceId);

            var value = new BedrockEmailId()
            {
                Id = emailId,
                UserId = userIdByDevice,
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

            LocalDB.UserIdDictionary[deviceId] = userId;

            await AwsKey.Context.SaveAsync(value);
        }

        return true;
    }

    [HttpPost]
    public async Task<bool> ReceiveCurrentProjectArchive()
    {
        var deviceId = GetDeviceId();
        var userId = await GetUserId(deviceId);

        var userSetting = await GetUserSetting(userId);

        var project = await GetProject(userSetting.CurrentProject);
        project.IsArchive = !project.IsArchive;
        project.ArchiveTick = DateTime.UtcNow.Ticks;
        await SaveProject(project);

        var projects = await ReceiveProjects(userId);

        userSetting.CurrentProject = projects.OrderByDescending(p => p.LastOpenTick).FirstOrDefault()?.Id ?? "0";

        await AwsKey.Context.SaveAsync(userSetting);

        return true;
    }

    [HttpPost]
    public async Task<bool> ReceiveShowDate()
    {
        var deviceId = GetDeviceId();
        var userId = await GetUserId(deviceId);

        var userSetting = await GetUserSetting(userId);

        userSetting.ShowDate = !userSetting.ShowDate;

        await AwsKey.Context.SaveAsync(userSetting);

        return true;
    }

    [HttpPost]
    public async Task<string> ReceiveTaskCount()
    {
        var deviceId = GetDeviceId();
        var userId = await GetUserId(deviceId);
        var userSetting = await GetUserSetting(userId);
        var project = userSetting.CurrentProject;

        var conditions = new List<ScanCondition>
        {
            new("Project", ScanOperator.Equal, project),
        };

        var contents = await AwsKey.Context.ScanAsync<BedrockContent>(conditions).GetRemainingAsync();

        var count = contents.Count;
        var doneCount = contents.Count(content => content.Done);

        return $"{doneCount}/{count}";
    }

    public async Task<List<BedrockProject>> ReceiveProjects(string userId)
    {
        var conditions = new List<ScanCondition>
        {
            new("Partition", ScanOperator.Equal, "0"),
            new("UserId", ScanOperator.Equal, userId),
            new("IsArchive", ScanOperator.NotEqual, true)
        };

        var bedrockProjects = await AwsKey.Context.ScanAsync<BedrockProject>(conditions).GetRemainingAsync();

        return bedrockProjects.ToList();
    }

    public async Task<BedrockProject> CreateProject(string userId, string projectName = "")
    {
        var projectId = Guid.NewGuid().ToString();

        if (string.IsNullOrEmpty(projectName))
            projectName = $"새로운 프로젝트-{projectId.Substring(0, 3)}";

        var project = new BedrockProject()
        {
            Id = projectId,
            UserId = userId,
            Partition = "0",
            Name = projectName,
            CreateTick = DateTime.UtcNow.Ticks,
            LastOpenTick = DateTime.UtcNow.Ticks,
        };

        await AwsKey.Context.SaveAsync(project);

        return project;
    }

    public async Task<BedrockProject> GetProject(string projectId)
    {
        var project = await AwsKey.Context.LoadAsync<BedrockProject>("0", projectId);
        return project;
    }

    public async Task<bool> SaveProject(BedrockProject project)
    {
        await AwsKey.Context.SaveAsync(project);
        return true;
    }


    public string GetDeviceId()
    {
        var deviceId = HttpContext.Request.Cookies["deviceId"];
        var version = HttpContext.Request.Cookies["version"];

        if (version != "0.1")
            return "";

        return deviceId ?? "";
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

        var code = GenerateRandomCode(4);

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

    private static string GenerateRandomCode(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        Random random = new Random();
        char[] codeChars = new char[length];

        for (int i = 0; i < length; i++)
        {
            codeChars[i] = chars[random.Next(chars.Length)];
        }

        return new string(codeChars);
    }

    public async Task<bool> VerifyCode(string email, string code)
    {
        code = code.ToUpper();

        var conditions = new List<ScanCondition>
        {
            new("Partition", ScanOperator.Equal, "bedrock"),
            new("Email", ScanOperator.Equal, email)
        };

        var allDocs = await AwsKey.Context.ScanAsync<EmailCode>(conditions).GetRemainingAsync();

        if (allDocs.Count == 0)
            return false;

        var result = allDocs.First();

        return result.Code == code;
    }

    private async Task<BedrockUserSetting> GetUserSetting(string userId)
    {
        if (LocalDB.UserSettingDictionary.TryGetValue(userId, out var setting))
            return setting;

        var userSetting = await AwsKey.Context.LoadAsync<BedrockUserSetting>("0", userId);

        if (userSetting == null)
        {
            userSetting = new BedrockUserSetting()
            {
                UserId = userId,
                Partition = "0",
                ShowDate = false,
                CurrentProject = "project-0"
            };

            await AwsKey.Context.SaveAsync(userSetting);
        }

        LocalDB.UserSettingDictionary.TryAdd(userId, userSetting);

        return userSetting;
    }

    private async Task<bool> SaveUserSetting(BedrockUserSetting userSetting)
    {
        await AwsKey.Context.SaveAsync(userSetting);
        return true;
    }

    public async Task<string> FirstSetting(string userId)
    {
        var thirdProject = await CreateProject(userId, "Bedrock 아이디어");

        await WriteContent(thirdProject.Id, "device id 쿠키로 구현");
        await WriteContent(thirdProject.Id, "uuid 4 사용하기");
        await WriteContent(thirdProject.Id, "로그인 구현하기");
        await WriteContent(thirdProject.Id, "이메일로 인증하게");
        await WriteContent(thirdProject.Id, "이메일 발송 구현하기");
        await WriteContent(thirdProject.Id, "템플릿 프로젝트");
        await WriteContent(thirdProject.Id, "사고 싶은 것");
        await WriteContent(thirdProject.Id, "맥미니 넣자");
        await WriteContent(thirdProject.Id, "Bedrock 아이디어");
        await WriteContent(thirdProject.Id, "Parent Content 구현하기");
        await WriteContent(thirdProject.Id, "Content 별로 div 따로 구현하기");
        await WriteContent(thirdProject.Id, "fetch로 각 content 수정할때 해당 부분만 변경되게");
        await WriteContent(thirdProject.Id, "체크처리 할때도 개별로 변경되게");
        await WriteContent(thirdProject.Id, "asp net Response Compression 적용하기");
        await WriteContent(thirdProject.Id, "프로젝트 폴더 구현하기");
        await WriteContent(thirdProject.Id, "프로젝트 내 프로젝트 구현?");
        await WriteContent(thirdProject.Id, "폴더처럼 작동해도 될 듯");
        await WriteContent(thirdProject.Id, "콘텐츠 수정 기능 구현하기");
        await WriteContent(thirdProject.Id, "클릭하면 input box로 변경되게");

        var secondProject = await CreateProject(userId, "사고 싶은 것");

        await WriteContent(secondProject.Id, "에어팟 맥스");
        await WriteContent(secondProject.Id, "맥미니 m4");
        await WriteContent(secondProject.Id, "삼성 건조기");
        await WriteContent(secondProject.Id, "로지텍 키보드 mx keys");

        var firstProject = await CreateProject(userId);

        await WriteContent(firstProject.Id, "안녕하세요🥳 새로 오신 것을 환영합니다!");
        await WriteContent(firstProject.Id, "Bedrock은 가장 강력한 Todo 앱입니다.  \n자세한 건 이 [소개 글](https://bedrock.es/home/about)을 읽어주세요");
        // await WriteContent(firstProject.Id, "Bedrock은 가장 강력한 Todo 앱입니다.  \n- **종단 간 암호화**로 완전한 보안  \n*(당신 외에 누구도 이 글을 읽을 수 없습니다)*  \n- **MarkDown** 문법 지원  \n- **완전한 동기화** *웹 , 안드로이드 , 아이폰 어디서든 사용하세요*  \n- **오픈 소스** *(우리는 절대로 죽지 않습니다!)*  \n  \n자세한 건 이 [소개 글](https://bedrock.es/home/about)을 읽어주세요");

        return firstProject.Id;
    }

    public async Task<BedrockContent> WriteContent(string projectId, string contentText)
    {
        contentText = contentText.Replace("<br>", "  \n");

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

        return value;
    }

    private string ContentToHtml(BedrockContent content, bool showDate)
    {
        //todo! 최적화하기 , 클라에서 해당 정보 가지고 있도록
        var dateTime = DateTime.MinValue.AddTicks(content.Tick);
        var timeSpan = DateTime.Now - DateTime.UtcNow;
        var fixedDateTime = dateTime.Add(timeSpan);

        var contentText = Markdown.ToHtml(content.Text).Replace("<p>", "").Replace("</p>", "");

        var resultContent = new StringBuilder();

        resultContent.Append($"<div >{contentText}</div>");

        if (showDate)
        {
            var dateText = $"""
                            <div class="click-color unselectable" style="height:100%; border-radius: 5px; cursor: pointer; margin-left: auto; padding-right: 6px; padding-left: 6px;">
                            <font color="#6c6c6c">
                            {(fixedDateTime.Date != DateTime.Now.Date ? $"{fixedDateTime:MM.dd.yy}" : $"{fixedDateTime:HH:mm}")}
                            </font>
                            </div>
                            """;

            resultContent.Append(dateText);
        }

        var text = $"""
                    <div style="max-width: 100%;">
                        <div class="ob-box" onclick='' style="width=100%; cursor: text; background-color:transparent;">
                            <div style="width:100%; height:100%; align-items: center;">
                             <div style="width:100%; height:100%; display: flex;">
                                 <div class="click-animate unselectable" onclick="ClickDone('{content.Id}')" style="cursor: pointer; min-width: 18px; max-width:18px; min-height: 18px; max-height: 18px; border: solid #cdd0d4;  border-width:1px; margin-top: 3px; margin-right: 10px; border-radius: 5px;"></div>
                                     <div style="display: flex; width:100%; border: none; outline: none;">
                                     {resultContent}
                                     </div>
                                 </div>
                            </div>
                        </div>
                    </div>
                    """;

        return text;
    }

    public async Task<string> GetUserId(string id)
    {
        if (LocalDB.UserIdDictionary.TryGetValue(id, out var userId))
            return userId;

        var conditions = new List<ScanCondition>
        {
            new("Id", ScanOperator.Equal, id)
        };

        var allDocs = await AwsKey.Context.ScanAsync<BedrockDeviceId>(conditions).GetRemainingAsync();

        if (allDocs.Count == 0)
        {
            var newUserId = Guid.NewGuid().ToString();

            var deviceId = new BedrockDeviceId()
            {
                Id = id,
                UserId = newUserId,
                Partition = "0"
            };

            await AwsKey.Context.SaveAsync(deviceId);

            return newUserId;
        }

        var result = allDocs.First();

        var resultUserId = result.UserId;

        LocalDB.UserIdDictionary[id] = resultUserId;

        return resultUserId;
    }

    public async Task<string> GetUserIdToEmail(string id)
    {
        var conditions = new List<ScanCondition>
        {
            new("Id", ScanOperator.Equal, id)
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
