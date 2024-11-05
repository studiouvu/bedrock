using System.Collections.Specialized;
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
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Bedrock.Views;

namespace Bedrock.Controllers;

public class UserData
{
    public string Id;
    public string Content;
    public string Version;
    public string Partition;
}
public class EmailCode
{
    public string Email;
    public int Code;
    public DateTime DateTime;
    public string Partition;
}
public class DeviceId
{
    public string Id;
    public string UserId;
    public string Partition;
}
public class ToastController : Controller
{
    private readonly ILogger<ToastController> _logger;
    private readonly IHubContext<MatchHub> _hubContext;

    public ToastController(ILogger<ToastController> logger, IHubContext<MatchHub> hubContext)
    {
        _logger = logger;
        _hubContext = hubContext;
    }

    public async Task<IActionResult> LoadUserData(string id)
    {
        var credentials = new BasicAWSCredentials(AwsKey.accessKey, AwsKey.secretKey);
        var client = new AmazonDynamoDBClient(credentials, RegionEndpoint.APNortheast1);

        var context = new DynamoDBContext(client);

        var userId = await GetUserId(id);

        var conditions = new List<ScanCondition>
        {
            new("Id", ScanOperator.Equal, userId)
        };

        var allDocs = await context.ScanAsync<UserData>(conditions).GetRemainingAsync();

        if (allDocs.Count == 0)
            return new EmptyResult();

        var result = allDocs.FirstOrDefault();

        return Content(result.Content);
    }

    public async Task<string> GetUserId(string id)
    {
        var credentials = new BasicAWSCredentials(AwsKey.accessKey, AwsKey.secretKey);
        var client = new AmazonDynamoDBClient(credentials, RegionEndpoint.APNortheast1);

        var context = new DynamoDBContext(client);

        var conditions = new List<ScanCondition>
        {
            new("Id", ScanOperator.Equal, id)
        };

        var allDocs = await context.ScanAsync<DeviceId>(conditions).GetRemainingAsync();

        if (allDocs.Count == 0)
        {
            var newUserId = await CreateUserId();

            var deviceId = new DeviceId()
            {
                Id = id,
                UserId = newUserId,
                Partition = "0"
            };

            await context.SaveAsync(deviceId);

            return newUserId;
        }

        var result = allDocs.First();

        return result.UserId;
    }

    [HttpPost]
    public async Task<IActionResult> SaveUserData(string id, string data)
    {
        var credentials = new BasicAWSCredentials(AwsKey.accessKey, AwsKey.secretKey);
        var client = new AmazonDynamoDBClient(credentials, RegionEndpoint.APNortheast1);

        var context = new DynamoDBContext(client);

        var userId = await GetUserId(id);

        var userData = new UserData
        {
            Id = userId,
            Content = data,
            Partition = "1"
        };

        await context.SaveAsync(userData);

        return Content("Success");
    }

    public async Task<string> CreateUserId()
    {
        var credentials = new BasicAWSCredentials(AwsKey.accessKey, AwsKey.secretKey);
        var client = new AmazonDynamoDBClient(credentials, RegionEndpoint.APNortheast1);

        var tableName = "Counters";
        var counterName = "UserId";

        var request = new UpdateItemRequest
        {
            TableName = tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                {
                    "CounterName", new AttributeValue
                    {
                        S = counterName
                    }
                },
                {
                    "Partition", new AttributeValue
                    {
                        S = "0"
                    }
                }
            },
            UpdateExpression = "ADD CounterValue :inc",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                {
                    ":inc", new AttributeValue
                    {
                        N = "1"
                    }
                }
            },
            ReturnValues = "UPDATED_NEW"
        };

        var response = await client.UpdateItemAsync(request);
        var counterValue = response.Attributes["CounterValue"].N;
        return counterValue;
    }

    public IActionResult Time()
    {
        return Content($"{DateTime.UtcNow.Ticks}");
    }

    public async Task<IActionResult> SendMail(string email)
    {
        var to = email;
        var from = "\"Studio uvu\" <app@studiouvu.com>";
        var message = new MailMessage(from, to);

        var randomCode = new Random();
        var code = randomCode.Next(1000, 9999);

        var mailbody = $"<div style='background-color: #f2f2f2; padding: 20px;'><img src=\"https://studiouvu.com/images/studiouvu-mail.png\"/><h1>UVU ID 연동 인증 코드</h1><p>아래의 코드를 입력해주세요.</p><h1>{code}</h1></div>";
        message.Subject = "UVU ID 연동 인증 코드";
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
            Partition = "1"
        };

        await context.SaveAsync(emailCode);

        return Content($"Success");
    }

    public async Task<IActionResult> VerifyCode(string email, int code)
    {
        var credentials = new BasicAWSCredentials(AwsKey.accessKey, AwsKey.secretKey);
        var client = new AmazonDynamoDBClient(credentials, RegionEndpoint.APNortheast1);

        var context = new DynamoDBContext(client);

        var conditions = new List<ScanCondition>
        { 
            new("Email", ScanOperator.Equal, email)
        };

        var allDocs = await context.ScanAsync<EmailCode>(conditions).GetRemainingAsync();

        if (allDocs.Count == 0)
            return new EmptyResult();

        var result = allDocs.First();

        if (result.Code == code)
            return Content($"true");

        return Content($"false");
    }

    public async Task NotifyPlayersOfMatch(string player1ConnectionId)
    {
        await _hubContext.Clients.All.SendAsync("MatchFound", "OpponentPlayerId");
    }

    public async Task NotifyPlayersOfMatch(string player1ConnectionId, string player2ConnectionId)
    {
        await _hubContext.Clients.Client(player1ConnectionId).SendAsync("MatchFound", "OpponentPlayerId");
        await _hubContext.Clients.Client(player2ConnectionId).SendAsync("MatchFound", "OpponentPlayerId");
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
