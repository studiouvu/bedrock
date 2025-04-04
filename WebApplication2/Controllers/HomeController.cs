using System.Diagnostics;
using System.Globalization;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;
using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.Runtime;
using Bedrock.Manage;
using Bedrock.Models;
using GEmojiSharp;
using Markdig;
using Markdig.Extensions.Emoji;
using Microsoft.AspNetCore.Cors;
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

    public async Task<IActionResult> Bedrock(string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId))
            deviceId = GetDeviceId();

        if (string.IsNullOrEmpty(deviceId))
        {
            ViewBag.Login = false;

            deviceId = Guid.NewGuid().ToString();
            await GetUserId(deviceId);
        }
        else
        {
            var userId = await GetUserId(deviceId);
            var emailId = await GetEmailId(userId);

            ViewBag.Login = string.IsNullOrEmpty(emailId) == false;

            var userSetting = await GetUserSetting(userId);
            var currentProject = await GetProject(userSetting.CurrentProject);

            if (currentProject == null)
            {
                var projects = await ReceiveProjects(userId);
                var targetProject = projects.OrderByDescending(project => project.LastOpenTick).FirstOrDefault()?.Id;
                if (targetProject != null)
                    userSetting.CurrentProject = targetProject;
            }
        }

        HttpContext.Session.SetString("deviceId", deviceId);
        HttpContext.Response.Cookies.Append("deviceId", deviceId);

        ViewBag.deviceId = deviceId;

        return View();
    }

    [HttpPost]
    public async Task<bool> ReceiveCreateProject([FromBody] DataModel data)
    {
        var deviceId = data.DeviceId;
        var userId = await GetUserId(deviceId);
        var newProject = await CreateProject(userId, ProjectType.Task);

        var userSetting = await GetUserSetting(userId);
        userSetting.CurrentProject = newProject.Id;

        await SaveUserSetting(userSetting);

        return true;
    }

    [HttpPost]
    public async Task<string> ReceiveText([FromBody] ContentTextDataModel model)
    {
        if (string.IsNullOrEmpty(model.Data))
            return string.Empty;

        var deviceId = model.DeviceId;
        var userId = await GetUserId(deviceId);
        var userSetting = await GetUserSetting(userId);

        var content = await WriteContent(userId, userSetting.CurrentProject, model.Data, model.Depth);

        var html = ContentToHtml(content, userSetting.ShowDoneTask);

        return html;
    }

    [HttpPost]
    public async Task<bool> ReceiveChangeProject([FromBody] DataModel data)
    {
        var deviceId = data.DeviceId;
        var userId = await GetUserId(deviceId);
        var userSetting = await GetUserSetting(userId);

        var projectId = data.Data;
        userSetting.CurrentProject = projectId;
        await SaveUserSetting(userSetting);

        var project = await GetProject(projectId);

        if (project == null)
            return false;

        project.LastOpenTick = DateTime.UtcNow.Ticks;
        await SaveProject(project);

        return true;
    }

    [HttpPost]
    public async Task<string> ReceiveUserId([FromBody] DataModel data)
    {
        var deviceId = data.DeviceId;
        var userId = await GetUserId(deviceId);
        var emailId = await GetEmailId(userId);
        return string.IsNullOrEmpty(emailId) ? userId.GetHashCode().ToString() : emailId;
    }

    [HttpPost]
    public async Task<string> ReceiveCurrentProjectName([FromBody] DataModel data)
    {
        var deviceId = data.DeviceId;
        var userId = await GetUserId(deviceId);
        var userSetting = await GetUserSetting(userId);

        if (userSetting.CurrentProject == "-")
            return "-";

        var bedrockProject = await GetProject(userSetting.CurrentProject);

        return bedrockProject.Name;
    }

    [HttpPost]
    public async Task<bool> ReceiveSendProjectName([FromBody] DataModel model)
    {
        var deviceId = model.DeviceId;
        var userId = await GetUserId(deviceId);
        var userSetting = await GetUserSetting(userId);

        if (userSetting.CurrentProject == "-")
            return false;

        var bedrockProject = await GetProject(userSetting.CurrentProject);

        var bedrockProjectName = model.Data;

        if (string.IsNullOrEmpty(bedrockProjectName))
        {
            var emoji = GetRandomEmoji();
            bedrockProjectName = emoji;
        }

        if (bedrockProject.Name == bedrockProjectName)
            return true;

        bedrockProject.Name = bedrockProjectName;
        await SaveProject(bedrockProject);

        return true;
    }

    [HttpPost]
    public async Task<bool> ReceiveSendDiaryContent([FromBody] DataModel model)
    {
        var deviceId = model.DeviceId;
        var userId = await GetUserId(deviceId);
        var userSetting = await GetUserSetting(userId);

        var projectId = userSetting.CurrentProject;

        if (projectId == "-")
            return false;

        var diaryContent = await AwsManager.DbContext.LoadAsync<BedrockDiaryContent>("0", projectId);

        var content = model.Data;

        if (diaryContent.Content == content)
            return true;

        diaryContent.Content = content;
        diaryContent.LastTick = DateTime.UtcNow.Ticks;

        await AwsManager.DbContext.SaveAsync(diaryContent);

        var queryText =
            $"Please summarize it as briefly as possible, and use real names for the individuals.\n" +
            $"\"{content}\"";
        //영어로 번역 "중요한 사건과 사람들을 정리해서 요약해줘 , 그리고 이 글을 쓴 사람의 관점 , 생각 , 상태를 요약해줘, 개인정보 보호는 안해도 됨 , 사람들은 실명으로 기록하기 , 영어로 기록해야함 , 고유명사들은 영어로 기록 할 필요 없음 , 요약은 최대한 간결하게 써야함 쓸데없이 길게 쓸 필요 없음"

        var gptText = await OpenAiControl.GetChat(queryText);

        diaryContent.Summary = gptText;

        await AwsManager.DbContext.SaveAsync(diaryContent);

        return true;
    }

    private string GetRandomEmoji()
    {
        var emojiList = new List<string>()
        {
            "🦄",
            "🐯",
            "🦊",
            "🐮",
            "🐻‍❄️",
            "🐹",
            "🏄",
            "👹",
            "🦝",
            "🐻",
            "🎏",
            "🐲",
            "🐙",
            "🥳",
            "🐼",
            "🎄",
            "🔥",
            "🌞",
            "🦕",
            "🎆",
            "🥊",
            "🍟",
            "🍔",
            "😶‍🌫️",
            "🌵",
            "🚃",
            "🥞",
            "🔔",
            "🐋",
            "🍄",
        };
        var emoji = emojiList[new Random().Next(0, emojiList.Count)];
        return emoji;
    }

    [HttpPost]
    public async Task<JsonResult> ReceiveLastProjectList([FromBody] DataModel model)
    {
        var deviceId = model.DeviceId;
        var userId = await GetUserId(deviceId);
        var projects = await ReceiveProjects(userId);
        var userSetting = await GetUserSetting(userId);

        var template = $"""
                            <div
                            class="click-color unselectable"
                            onclick="ChangeProject('projectId', 'projectNameRaw')"
                            style="cursor: pointer; height: 100%; background-color: #1f1f1f; padding: 6px 9px; border-radius: 10px; margin-right: 6px;">
                                  <div class="text-center">
                                      projectName
                                  </div>
                            </div>
                        """;

        var data = new
        {
            html = template,
            content = projects
                .Where(project => project.ProjectType == ProjectType.Task)
                .OrderByDescending(project => project.LastOpenTick)
                .Where(project => project.Id != userSetting.CurrentProject)
                .Take(10)
                .Select(project => new
                {
                    id = project.Id,
                    name = project.Name,
                }).ToList(),
        };
        return Json(data);
    }

    [HttpPost]
    public async Task<JsonResult> ReceiveProjectList([FromBody] DataModel model)
    {
        var deviceId = model.DeviceId;
        var userId = await GetUserId(deviceId);
        var userSetting = await GetUserSetting(userId);
        var projects = (await ReceiveProjects(userId))
            .Where(project => project.ProjectType == ProjectType.Task);

        var template = $"""
                            <div
                                class="click-color unselectable"
                               onclick="ChangeProject('projectId','projectNameRaw')"
                               style="width:95%; cursor: pointer; background-color: backgroundColor; border-radius: 10px; padding: 4px 8px;">
                               projectName
                            </div>
                        """;

        var data = new
        {
            html = template,
            content = projects.OrderBy(project => ReplaceEmojis(project.Name, "0")).Select(project => new
            {
                id = project.Id,
                name = project.Name,
                backgroundColor = project.Id == userSetting.CurrentProject ? "#1f1f1f" : "transparent",
            }).ToList(),
        };
        return Json(data);
    }

    [HttpPost]
    public async Task<bool> ClickDone([FromBody] DataModel model)
    {
        var id = model.Data;

        var content = await AwsManager.DbContext.LoadAsync<BedrockContent>("0", id);

        if (content == null)
            return false;

        content.Done = !content.Done;
        content.DoneTick = DateTime.UtcNow.Ticks;

        await AwsManager.DbContext.SaveAsync(content);

        return true;
    }

    [HttpPost]
    public async Task<JsonResult> ReceiveFullContent([FromBody] DataModel model)
    {
        var deviceId = model.DeviceId;
        var userId = await GetUserId(deviceId);
        var userSetting = await GetUserSetting(userId);

        var projectId = userSetting.CurrentProject;

        var project = await GetProject(projectId);

        if (project == null)
        {
            var data = new
            {
                content = "",
                projectType = "Task",
            };
            return Json(data);
        }

        if (project.ProjectType == ProjectType.Task)
            return await GetTaskProjectHtml(userId, projectId, userSetting);
        else
            return await GetDiaryProjectHtml(userId, projectId, userSetting);
    }

    private async Task<JsonResult> GetTaskProjectHtml(string userId, string projectId, BedrockUserSetting userSetting)
    {
        // 보조 인덱스 이름 설정
        var indexName = "Partition-Project-index";

        var filter = new QueryFilter("Project", QueryOperator.Equal, projectId);
        filter.AddCondition("Partition", QueryOperator.Equal, "0");

        var query = new QueryOperationConfig
        {
            IndexName = "Partition-Project-index",
            Filter = filter,
        };

        var contents = await AwsManager.DbContext.FromQueryAsync<BedrockContent>(query).GetRemainingAsync();

        if (contents.Count == 0)
        {
            var nullData = new
            {
                content = "",
                projectType = "Task",
            };
            return Json(nullData);
        }

        StringBuilder builder = new();

        if (!userSetting.ShowDoneTask)
            contents = contents.Where(content => content.Done == false).ToList();

        foreach (var content in contents.Where(content => !content.Done).OrderBy(content => content.Tick))
        {
            var html = ContentToHtml(content, userSetting.ShowDoneTask);
            builder.Append(html);
        }

        if (userSetting.ShowDoneTask && contents.Any(content => content.Done))
        {
            builder.Append("<div style='min-height:6px; width:100%;'></div>");
            builder.Append("<div style='min-height:1px; width:100%; background-color:#1f1f1f;'></div>");
            // builder.Append("<font color=\"#6c6c6c\">Completed</font>");

            foreach (var content in contents.Where(content => content.Done).OrderBy(content => -content.DoneTick))
            {
                var html = ContentToHtml(content, userSetting.ShowDoneTask);
                builder.Append(html);
            }
        }

        UpdateGptContent(userId);

        var data = new
        {
            content = builder.ToString(),
            projectType = "Task",
        };
        return Json(data);
    }

    private async Task<JsonResult> GetDiaryProjectHtml(string userId, string projectId, BedrockUserSetting userSetting)
    {
        var diaryContent = await AwsManager.DbContext.LoadAsync<BedrockDiaryContent>("0", projectId);

        if (diaryContent == null)
        {
            diaryContent = new BedrockDiaryContent()
            {
                Partition = "0",
                ProjectId = projectId,
                Content = "",
                UserId = userId,
                LastTick = DateTime.UtcNow.Ticks,
            };

            await AwsManager.DbContext.SaveAsync(diaryContent);
        }

        // var contentText = Markdown.ToHtml(diaryContent.Content).Replace("<p>", "").Replace("</p>", "");

        StringBuilder builder = new();
        builder.Append("<div style='min-height:6px; width:100%;'></div>");
        builder.Append("<div style='min-height:1px; width:100%; margin-bottom: 15px; background-color:#1f1f1f;'></div>");
        builder.Append("<div id='diary-content' style='width:100%; min-height:500px; padding-bottom:100px; outline: none;' contenteditable='true' onblur='InputDiaryContent()'>" +
                       $"{diaryContent.Content}</div>");

        var data = new
        {
            content = builder.ToString(),
            projectType = "Diary",
        };
        return Json(data);
    }

    [HttpPost]
    public async Task<bool> ReceiveCreateNewDiary([FromBody] DataModel data)
    {
        var deviceId = data.DeviceId;
        var userId = await GetUserId(deviceId);

        var newProject = await CreateProject(userId, ProjectType.Diary);

        var userSetting = await GetUserSetting(userId);
        userSetting.CurrentProject = newProject.Id;

        await SaveUserSetting(userSetting);

        return true;
    }


    [HttpPost]
    public async Task<string> ReceiveGptContent([FromBody] DataModel model)
    {
        var deviceId = model.DeviceId;
        var userId = await GetUserId(deviceId);

        var emailId = await GetEmailId(userId);

        var isLogin = string.IsNullOrEmpty(emailId) == false;

        if (!isLogin)
            return "로그인이 필요합니다";

        await ReceiveChangeProject(new DataModel()
        {
            DeviceId = model.DeviceId,
            Data = "-",
        });

        var secretary = await AwsManager.DbContext.LoadAsync<BedrockSecretary>("0", userId);

        var dateTime = DateTime.MinValue.AddTicks(secretary.lastUpdateTick);
        var timeSpan = DateTime.Now - DateTime.UtcNow;
        var fixedDateTime = dateTime.Add(timeSpan);

        return $"<font color=\"#919191\">1시간마다 자동으로 업데이트됩니다.</font><br><br>{secretary.Content}<br><br><font color=\"#919191\">업데이트 된 시간 : {fixedDateTime:yy.MM.dd HH:mm}</font><br>";
    }

    [HttpPost]
    public async Task<bool> ReceiveGoToTask([FromBody] DataModel model)
    {
        var deviceId = model.DeviceId;
        var userId = await GetUserId(deviceId);
        var userSetting = await GetUserSetting(userId);
        var currentProject = await GetProject(userSetting.CurrentProject);

        if (currentProject != null)
            return false;
        
        var projects = await ReceiveProjects(userId);
        var targetProject = projects.OrderByDescending(project => project.LastOpenTick).FirstOrDefault()?.Id;
        if (targetProject != null)
            userSetting.CurrentProject = targetProject;
        
        return true;
    }

    private async Task<bool> UpdateGptContent(string userId)
    {
        var emailId = await GetEmailId(userId);

        var isLogin = string.IsNullOrEmpty(emailId) == false;

        if (!isLogin)
            return false;

        var secretary = await AwsManager.DbContext.LoadAsync<BedrockSecretary>("0", userId);

        if (secretary != null)
        {
            var tick = (DateTime.UtcNow.Ticks - secretary.lastUpdateTick);
            var minutes = TimeSpan.FromTicks(tick).TotalMinutes;
            if (minutes < 60)
                return false;
        }

        if (secretary == null)
        {
            secretary = new BedrockSecretary()
            {
                Partition = "0",
                UserId = userId,
                lastUpdateTick = DateTime.UtcNow.Ticks,
                Content = "",
            };
        }
        else
        {
            secretary.lastUpdateTick = DateTime.UtcNow.Ticks;
            secretary.Content = "업데이트 중입니다, 잠시만 기다려주세요<br>" + secretary.Content;
        }

        await AwsManager.DbContext.SaveAsync(secretary);

        var projectList = await ReceiveProjects(userId);
        var projects = projectList.OrderByDescending(p => p.LastOpenTick);

        StringBuilder builder = new();

        var conditions = new List<ScanCondition>
        {
            new("UserId", ScanOperator.Equal, userId),
        };

        var userContents = await AwsManager.DbContext.ScanAsync<BedrockContent>(conditions).GetRemainingAsync();

        foreach (var project in projects.Where(project => project.ProjectType == ProjectType.Task))
        {
            if (project.IsArchive)
                continue;

            builder.Append($"(Project Name: {project.Name}, Contents: ");

            var contents = userContents
                .Where(content => content.Project == project.Id);

            foreach (var content in contents
                         .Where(content => !content.IsTemplate)
                         .Where(content => content.Done == false)
                         .OrderBy(content => content.Tick))
            {
                var dateTime = DateTime.MinValue.AddTicks(content.Tick);
                var timeSpan = DateTime.Now - DateTime.UtcNow;
                var fixedDateTime = dateTime.Add(timeSpan);

                var doneDateTime = DateTime.MinValue.AddTicks(content.DoneTick);
                var fixedDoneDateTime = doneDateTime.Add(timeSpan);

                var t = $"CreateTime: {fixedDateTime:yy-MM-dd} , DoneTime: {fixedDoneDateTime:yy-MM-dd} , Content: {content.Text}), Depth: {content.depth}),";
                builder.Append(t);
            }

            builder.Append($")\n");
        }


        var userSetting = await GetUserSetting(userId);

        var summaryText = userSetting.DiarySummary;

        if ((DateTime.Now.Date != userSetting.DiarySummaryUpdateTime.Date))
        {
            StringBuilder queryBuilder = new();

            foreach (var project in projects.Where(project => project.ProjectType == ProjectType.Diary))
            {
                var diaryContent = await AwsManager.DbContext.LoadAsync<BedrockDiaryContent>("0", project.Id);
                if (diaryContent == null)
                    continue;
                queryBuilder.Append($"(Diary Name: {project.Name}, Content: {diaryContent.Summary}),");
            }

            var a = "Please organize and summarize the important events and people in chronological order, and summarize the perspective, thoughts, and state of the person who wrote this text. There is no need to protect personal information; record people by their real names. The summary should be written in English, but there is no need to translate proper nouns into English. The summary should be as concise as possible; there is no need to make it unnecessarily long.";
            var diaryQuery = $"{a} \n ({queryBuilder})";

            summaryText = await OpenAiControl.GetChat(diaryQuery);
            userSetting.DiarySummary = summaryText;
            userSetting.DiarySummaryUpdateTime = DateTime.Now;
            await SaveUserSetting(userSetting);
        }

        var originText = $"""
                          Today is {DateTime.Now:yy-MM-dd HH:mm:ss}.
                          Please organize and select 10 tasks that need to be done immediately today in order of importance as you see fit, and include the reason for each one. These tasks should be beneficial to me from a long-term perspective, contributing to my personal growth and having a positive impact on my life. Present this in Korean.

                          If a task's Depth is higher than the task above it, it means it's a subtask of that task. ProjectName may indicate the deadline; for example, "24.11.25" means the task is due by November 25, 2024, and "24.11" means it's a task within November 2024 without a specific date.

                          Attach the project name next to each task title in the format "1. Task Name - Project Name," and write the reason below on a new line.

                          After that, please select 5 tasks that may not be immediate for today but are important for my life in the long term.

                          Next, group the tasks by project within the same category, and select 10 important tasks per category, providing the reasons for each.

                          Lastly, provide me with advice that could be helpful to me.
                          """;
        //오늘은 {DateTime.Now:yy-MM-dd}일이야, 너가 생각하기에 중요한 순서대로 오늘 당장 해야 할 일을 정리해서 10개를 뽑아줘, 그리고 각각 그 이유도 같이 붙여줘 , 한국어로 , Depth는 상단의 Task의 Depth보다 높을 경우 그 task의 하위 task라는 것을 뜻해 , ProjectName은 기한을 뜻할 수도 있어 , 24.11.25 이런건 24년 11월 25일까지인거고 24.11 이건 24년 11월 중으로 일자는 확정되지 않은 task라는 것이야 ,  각 할일의 제목 옆에 프로젝트 이름을 붙여주고 "1. 태스크 이름 - 프로젝트 이름" 이런식으로 그리고 이유를 줄 바꿔서 밑에 써주고 , 그리고 그 다음엔 너가 보기에 같은 분류의 프로젝트 별로 일감들을 묶어서 분류 별 중요한 일 10가지를 뽑아서 이유와 함께 알려줘 , 마지막에는 나에게 도움이 될만한 조언을 적어줘
        var example = """
                      예시 : "
                      오늘 해야 할 일 10가지:

                      1. 통장 사본 제출하기 - 🥞.Daily
                          - 오늘 오후 6시까지 제출해야 하므로 매우 긴급합니다.
                      2. 베드락 iOS 출시 - 🦕24.11.12
                          - 오늘이 출시 예정일이므로 반드시 마무리해야 합니다.
                      3. 베드락 앱 추출하기 - 🦕24.11.12
                          - iOS 출시를 위해 필요한 단계입니다.
                      4. 베드락 폴더 기능 구현 - 🦕24.11.12
                          - 앱의 주요 기능으로 출시 전에 완료해야 합니다.
                      5. 이미 작성된 태스크 수정 및 탭 기능 추가 - 🦕24.11.12
                          - 사용자 경험 향상을 위해 필요한 작업입니다.
                      6. 어도비 결제 취소 및 할인받기 - 🥞.Daily
                          - 불필요한 지출을 막고 할인 혜택을 받기 위해 오늘 처리해야 합니다.
                      7. 베드락 프로젝트 마무리하기 - 👹24.11.11
                          - 프로젝트를 끝내기 위해 남은 작업들을 정리해야 합니다.
                      8. 안드로이드 내부 테스트 초대하기 - 🥞.Daily
                          - 앱의 품질 향상을 위해 테스트가 필요합니다.
                      9. 로그인 구현 과정 블로그 올리기 - 🐹11월
                          - 예정된 포스팅으로, 일정에 맞게 작성해야 합니다.
                      10. 진근 선배에게 연락하기 - 🐹11월
                          - 중요한 전달 사항이 있을 수 있으므로 빠르게 연락해야 합니다.

                      **카테고리별 그룹화 및 중요한 작업들**

                      ### 베드락 프로젝트

                      1. **베드락 iOS 출시** - 🦕24.11.12
                          - 오늘이 출시일이므로 최우선으로 처리해야 합니다.
                      2. **베드락 앱 추출하기** - 🦕24.11.12
                          - 출시를 위해 필요한 과정입니다.
                      3. **베드락 폴더 기능 구현** - 🦕24.11.12
                          - 사용자 편의성을 높이기 위한 핵심 기능입니다.
                      4. **이미 작성된 태스크 수정 및 탭 기능 추가** - 🦕24.11.12
                          - 앱의 완성도를 높이기 위한 작업입니다.
                      5. **베드락 프로젝트 마무리하기** - 👹24.11.11
                          - 프로젝트의 성공적인 완료를 위해 남은 사항들을 정리해야 합니다.

                      ### 일상 업무

                      1. **통장 사본 제출하기** - 🥞.Daily
                          - 오늘 오후 6시까지 꼭 제출해야 하므로 긴급합니다.
                      2. **어도비 결제 취소 및 할인받기** - 🥞.Daily
                          - 불필요한 비용 지출을 막고 할인 혜택을 받기 위해 오늘 처리해야 합니다.

                      ### 연락

                      1. **진근 선배에게 연락하기** - 🐹11월
                          - 중요한 사항을 전달하거나 확인하기 위해 빠른 연락이 필요합니다.
                      2. **영현이와 약속 잡기** - 🐹11월
                          - 일정 조율을 위해 연락이 필요합니다.

                      ### 토스트 클럽

                      1. **안드로이드 내부 테스트 초대하기** - 🥞.Daily
                          - 앱의 기능 테스트와 피드백 수집을 위해 필요합니다.

                      **도움이 될 만한 조언** 오늘은 중요한 마감일과 급한 업무들이 많으니 우선순위를 정하여 하나씩 처리해 보세요. 가장 긴급한 일부터 시작하고, 중간중간 휴식을 취하며 효율적으로 업무를 진행하시길 바랍니다. 성공적인 하루 보내세요!  
                        
                      "
                      \n
                      """;
        var queryText = $"{originText}\n{example}\n{builder}\nRequester Information for the Context of the Tasks:{summaryText}";

        var resultText = "";
        var gptText = await OpenAiControl.GetChat(queryText);
        resultText += gptText;

        var contentText = Markdown.ToHtml(resultText).Replace("<p>", "").Replace("</p>", "");

        var bedrockSecretary = new BedrockSecretary()
        {
            Partition = "0",
            UserId = userId,
            lastUpdateTick = DateTime.UtcNow.Ticks,
            Content = contentText,
        };

        await AwsManager.DbContext.SaveAsync(bedrockSecretary);

        return true;
    }

    [HttpPost]
    public async Task<ActionResult> ReceiveDiaryContent([FromBody] DataModel model)
    {
        var deviceId = model.DeviceId;
        var userId = await GetUserId(deviceId);

        await ReceiveChangeProject(new DataModel()
        {
            DeviceId = model.DeviceId,
            Data = "-",
        });

        var projects = (await ReceiveProjects(userId))
            .Where(project => project.ProjectType == ProjectType.Diary)
            .OrderByDescending(project => ReplaceEmojis(project.Name, "").Replace(".", ""));

        ViewBag.DiaryList = projects
            .Select(project => new
            {
                id = project.Id,
                name = project.Name,
                createDate = DateTime.MinValue.AddTicks(project.CreateTick).ToString("yy.MM.dd"),
            }).ToList();

        return View("Element/DiaryHome");
    }

    [HttpPost]
    public async Task<bool> ReceiveDeleteAccount([FromBody] DataModel model)
    {
        var deviceId = model.DeviceId;
        var userId = await GetUserId(deviceId);
        var emailId = await GetEmailUser(userId);

        if (emailId == null)
            return false;

        await AwsManager.DbContext.DeleteAsync(emailId);

        var deviceIdUser = await AwsManager.DbContext.LoadAsync<BedrockDeviceId>("0", deviceId);
        await AwsManager.DbContext.DeleteAsync(deviceIdUser);

        LocalDB.UserIdDictionary.Remove(deviceId);

        return true;
    }

    [HttpPost]
    public async Task<ActionResult> ReceiveSettings([FromBody] DataModel model)
    {
        return View("Element/Settings");
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
        var emailId = model.Content.ToLower();
        var code = model.Data;

        var verify = await VerifyCode(emailId, code);

        if (verify == false)
            return false;

        var userId = await GetUserIdToEmail(emailId);

        var deviceId = model.DeviceId;

        if (string.IsNullOrEmpty(userId))
        {
            var userIdByDevice = await GetUserId(deviceId);

            var value = new BedrockEmailId()
            {
                Id = emailId,
                UserId = userIdByDevice,
                Partition = "0",
            };

            await AwsManager.DbContext.SaveAsync(value);
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

            await AwsManager.DbContext.SaveAsync(value);
        }

        return true;
    }

    [HttpPost]
    public async Task<bool> ReceiveCurrentProjectArchive([FromBody] DataModel data)
    {
        var deviceId = data.DeviceId;
        var userId = await GetUserId(deviceId);

        var userSetting = await GetUserSetting(userId);

        var project = await GetProject(userSetting.CurrentProject);
        project.IsArchive = !project.IsArchive;
        project.ArchiveTick = DateTime.UtcNow.Ticks;
        await SaveProject(project);

        var projects = await ReceiveProjects(userId);

        userSetting.CurrentProject = projects.OrderByDescending(p => p.LastOpenTick).FirstOrDefault()?.Id ?? "0";

        await AwsManager.DbContext.SaveAsync(userSetting);

        return true;
    }

    [HttpPost]
    public async Task<bool> ReceiveShowDate([FromBody] DataModel data)
    {
        var deviceId = data.DeviceId;
        var userId = await GetUserId(deviceId);

        var userSetting = await GetUserSetting(userId);

        userSetting.ShowDate = !userSetting.ShowDate;

        await AwsManager.DbContext.SaveAsync(userSetting);

        return true;
    }

    [HttpPost]
    public async Task<bool> ReceiveShowDoneTask([FromBody] DataModel data)
    {
        var deviceId = data.DeviceId;
        var userId = await GetUserId(deviceId);
        var userSetting = await GetUserSetting(userId);

        userSetting.ShowDoneTask = !userSetting.ShowDoneTask;

        await AwsManager.DbContext.SaveAsync(userSetting);

        return true;
    }

    [HttpPost]
    public async Task<string> ReceiveTaskCount([FromBody] DataModel data)
    {
        var deviceId = data.DeviceId;
        var userId = await GetUserId(deviceId);
        var userSetting = await GetUserSetting(userId);
        var project = userSetting.CurrentProject;

        var conditions = new List<ScanCondition>
        {
            new("Project", ScanOperator.Equal, project),
        };

        var contents = await AwsManager.DbContext.ScanAsync<BedrockContent>(conditions).GetRemainingAsync();

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
            new("IsArchive", ScanOperator.NotEqual, true),
        };

        var bedrockProjects = await AwsManager.DbContext.ScanAsync<BedrockProject>(conditions).GetRemainingAsync();

        return bedrockProjects.ToList();
    }

    public async Task<BedrockProject> CreateProject(string userId, ProjectType projectType, string projectName = "")
    {
        var projectId = Guid.NewGuid().ToString();

        if (string.IsNullOrEmpty(projectName))
        {
            var newEmoji = GetRandomEmoji();
            projectName = $"{newEmoji}{DateTime.Now:yy.MM.dd}"; //새로운 프로젝트-{projectId.Substring(0, 3)}
        }

        var project = new BedrockProject()
        {
            Id = projectId,
            UserId = userId,
            Partition = "0",
            Name = projectName,
            CreateTick = DateTime.UtcNow.Ticks,
            LastOpenTick = DateTime.UtcNow.Ticks,
            ProjectType = projectType,
        };

        await AwsManager.DbContext.SaveAsync(project);

        return project;
    }

    public async Task<BedrockProject> GetProject(string projectId)
    {
        if (string.IsNullOrEmpty(projectId))
            return null;
        var project = await AwsManager.DbContext.LoadAsync<BedrockProject>("0", projectId);
        return project;
    }

    public async Task<bool> SaveProject(BedrockProject project)
    {
        await AwsManager.DbContext.SaveAsync(project);
        return true;
    }

    public string GetDeviceId()
    {
        var deviceId = HttpContext.Request.Query["deviceId"].ToString();

        if (string.IsNullOrEmpty(deviceId))
            deviceId = HttpContext.Session.GetString("deviceId");

        if (string.IsNullOrEmpty(deviceId))
            deviceId = HttpContext.Request.Cookies["deviceId"];

        return deviceId ?? "";
    }

    public async Task<string> GetEmailId(string userId)
    {
        var emailUser = await GetEmailUser(userId);
        if (emailUser == null)
            return string.Empty;
        return emailUser.Id;
    }

    public async Task<BedrockEmailId> GetEmailUser(string userId)
    {
        var conditions = new List<ScanCondition>
        {
            new("UserId", ScanOperator.Equal, userId)
        };

        var emailIds = await AwsManager.DbContext.ScanAsync<BedrockEmailId>(conditions).GetRemainingAsync();

        return emailIds.Count == 0 ? null : emailIds.First();
    }

    public async Task<bool> SendMail(string email)
    {
        email = email.ToLower();

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
        var basicCredential1 = new System.Net.NetworkCredential("app@studiouvu.com", Environment.GetEnvironmentVariable("GOOGLE_SMTP_PASSWORD"));
        
        client.EnableSsl = true;
        client.UseDefaultCredentials = false;
        client.Credentials = basicCredential1;

        await client.SendMailAsync(message);

        var credentials = new BasicAWSCredentials(AwsManager.accessKey, AwsManager.secretKey);
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

        var allDocs = await AwsManager.DbContext.ScanAsync<EmailCode>(conditions).GetRemainingAsync();

        if (allDocs.Count == 0)
            return false;

        var result = allDocs.First();

        return result.Code == code;
    }

    private async Task<BedrockUserSetting> GetUserSetting(string userId)
    {
        if (LocalDB.UserSettingDictionary.TryGetValue(userId, out var setting))
            return setting;

        var userSetting = await AwsManager.DbContext.LoadAsync<BedrockUserSetting>("0", userId);

        if (userSetting == null)
        {
            userSetting = new BedrockUserSetting()
            {
                UserId = userId,
                Partition = "0",
                ShowDate = false,
                CurrentProject = "project-0"
            };

            await AwsManager.DbContext.SaveAsync(userSetting);
        }

        LocalDB.UserSettingDictionary.TryAdd(userId, userSetting);

        return userSetting;
    }

    private async Task<bool> SaveUserSetting(BedrockUserSetting userSetting)
    {
        await AwsManager.DbContext.SaveAsync(userSetting);
        return true;
    }

    public async Task<string> FirstSetting(string userId)
    {
        var secondProject = await CreateProject(userId, ProjectType.Task, "사고 싶은 것");

        await WriteContent(userId, secondProject.Id, "에어팟 맥스", isTemplate: true);
        await WriteContent(userId, secondProject.Id, "맥미니 m4", isTemplate: true);
        await WriteContent(userId, secondProject.Id, "삼성 건조기", isTemplate: true);

        //todo! 지역별로 설정 필요
        var firstProject = await CreateProject(userId, ProjectType.Task, $"🦊{DateTime.Now:yy.MM.dd}");

        await WriteContent(userId, firstProject.Id, "안녕하세요🥳 새로 오신 것을 환영합니다!", isTemplate: true);
        await WriteContent(userId, firstProject.Id, "자유롭게 할 일을 추가해 보세요!", isTemplate: true);
        // await WriteContent(userId, firstProject.Id, "Bedrock은 가장 강력한 Todo 앱입니다.  \n자세한 건 아래 소개글을 읽어주세요", isTemplate: true);
        // await WriteContent(firstProject.Id, "Bedrock은 가장 강력한 Todo 앱입니다.  \n- **종단 간 암호화**로 완전한 보안  \n*(당신 외에 누구도 이 글을 읽을 수 없습니다)*  \n- **MarkDown** 문법 지원  \n- **완전한 동기화** *웹 , 안드로이드 , 아이폰 어디서든 사용하세요*  \n- **오픈 소스** *(우리는 절대로 죽지 않습니다!)*  \n  \n자세한 건 이 [소개 글](https://bedrock.es/home/about)을 읽어주세요");

        return firstProject.Id;
    }

    public async Task<BedrockContent> WriteContent(string userId, string projectId, string contentText, int depth = 0, bool isTemplate = false)
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
            depth = depth,
            UserId = userId,
            IsTemplate = isTemplate,
        };

        await AwsManager.DbContext.SaveAsync(value);

        return value;
    }

    private string ContentToHtml(BedrockContent content, bool showDoneTask)
    {
        //todo! 최적화하기 , 클라에서 해당 정보 가지고 있도록
        var dateTime = DateTime.MinValue.AddTicks(content.Tick);
        var timeSpan = DateTime.Now - DateTime.UtcNow;
        var fixedDateTime = dateTime.Add(timeSpan);

        var contentText = Markdown.ToHtml(content.Text).Replace("<p>", "").Replace("</p>", "");

        var resultContent = new StringBuilder();

        resultContent.Append($"{contentText}");

        var dateText = $"""
                        <div class="hover click-color unselectable" style="border-radius: 5px; cursor: pointer; margin-left:6px; padding-right: 6px; padding-left: 6px;">
                        <font color="#6c6c6c">
                        작성
                        {(fixedDateTime.Date != DateTime.Now.Date ?
                            fixedDateTime.Date.Year == DateTime.Now.Year ?
                                fixedDateTime.ToString("MM/dd", CultureInfo.InvariantCulture)
                                : fixedDateTime.ToString("yy/MM/dd", CultureInfo.InvariantCulture)
                            : $"{fixedDateTime:HH:mm}")}
                        </font>
                        </div>
                        """;

        var tabText = "";

        for (int i = 0; i < content.depth; i++)
        {
            tabText += "&nbsp;&nbsp;&nbsp;&nbsp;";
        }

        var checkBoxDiv = content.Done ?
            $"""
             <div class="click-animate unselectable" onclick="ClickRecover('{content.Id}')" 
               style="cursor: pointer; min-width: 18px; max-width:18px; min-height: 18px; max-height: 18px;
                margin-right: 10px;">
                ✅
                </div>
             """
            :
            $"""
             <div class="click-animate unselectable" onclick="ClickDone('{content.Id}','{(showDoneTask ? "false" : "true")}')" 
               style="cursor: pointer; min-width: 18px; max-width:18px; min-height: 18px; max-height: 18px;
                margin-right: 10px; border: solid #cdd0d4; border-width:1px; margin-top: 3px; border-radius: 5px; ">
                </div>
             """;

        if (content.Done)
            resultContent = new StringBuilder($"<span style=\"color:#6c6c6c; display:inline;\">{resultContent}</span>");

        var text = $"""
                    <div id='{content.Id}' style="max-width: 100%;">
                        <div class="ob-box" onclick='' style="width:100%; cursor: text; background-color:transparent;">
                            <div style="width:100%; height:100%; align-items: center;">
                             <div style="width:100%; height:100%; display: flex;">
                                 {tabText}
                                 {checkBoxDiv}
                                     <div class="hover-container" style="display: flex; width:100%; border: none; outline: none;">
                                     {resultContent}
                                     </div>
                                 </div>
                            </div>
                        </div>
                    </div>
                    """;

        return text;
    }

    public async Task<bool> ExistUserId(string id)
    {
        if (LocalDB.UserIdDictionary.TryGetValue(id, out var userId))
            return true;

        var conditions = new List<ScanCondition>
        {
            new("Id", ScanOperator.Equal, id)
        };

        var deviceIds = await AwsManager.DbContext.ScanAsync<BedrockDeviceId>(conditions).GetRemainingAsync();

        return deviceIds.Count != 0;
    }

    public async Task<string> GetUserId(string id)
    {
        if (LocalDB.UserIdDictionary.TryGetValue(id, out var userId))
            return userId;

        var conditions = new List<ScanCondition>
        {
            new("Id", ScanOperator.Equal, id)
        };

        var allDocs = await AwsManager.DbContext.ScanAsync<BedrockDeviceId>(conditions).GetRemainingAsync();

        if (allDocs.Count == 0)
        {
            var newUserId = Guid.NewGuid().ToString();

            var deviceId = new BedrockDeviceId()
            {
                Id = id,
                UserId = newUserId,
                Partition = "0"
            };

            await AwsManager.DbContext.SaveAsync(deviceId);

            var firstProject = await FirstSetting(newUserId);

            var userSetting = await GetUserSetting(newUserId);
            userSetting.CurrentProject = firstProject;
            await SaveUserSetting(userSetting);

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

        var allDocs = await AwsManager.DbContext.ScanAsync<BedrockEmailId>(conditions).GetRemainingAsync();

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

    public static string ReplaceEmojis(string text, string replace)
    {
        var result = Regex.Replace(text, Emoji.RegexPattern, replace); // Lorem  ipsum
        return result;
    }
}
