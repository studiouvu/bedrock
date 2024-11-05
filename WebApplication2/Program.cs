using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.Runtime;
using WebApplication2;
using WebApplication2.Controllers;
using WebApplication2.Models;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();
builder.Services.AddRouting(options =>
    {
        options.LowercaseUrls = true;
    }
);
// 세션 서비스 추가
builder.Services.AddSession(options =>
{
    options.Cookie.Name = ".YourApp.Session";
    options.IdleTimeout = TimeSpan.FromMinutes(20);
    options.Cookie.IsEssential = true; // GDPR 등의 이유로 필요
    
    var credentials = new BasicAWSCredentials(AwsKey.accessKey, AwsKey.secretKey);
    AwsKey.Client = new AmazonDynamoDBClient(credentials, RegionEndpoint.APNortheast1);
    AwsKey.Context = new DynamoDBContext(AwsKey.Client);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

// app.Use(async (context, next) =>
// {
//     if (context.Request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase))
//     {
//         var ipAddress = context.Request.Headers["X-Forwarded-For"].FirstOrDefault() ?? context.Connection.RemoteIpAddress.ToString();
//         var requestedUrl = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}{context.Request.QueryString}";
//     
//         // 콘솔에 로그 출력
//         Console.WriteLine($"IP : {ipAddress} / {requestedUrl}");
//     }
//     
//     await next.Invoke(); //다음 pipeline 호출
// });

app.UseRouting();

app.UseAuthorization();

app.UseSession();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Bedrock}/{id?}");

app.MapControllers();

app.Run();
