using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.Runtime;
using Bedrock;
using Bedrock.Controllers;
using Microsoft.AspNetCore.ResponseCompression;
using OpenAI.Chat;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();
builder.Services.AddRouting(options =>
    {
        options.LowercaseUrls = true;
    }
);
builder.Services.AddResponseCompression(options =>
{
    options.Providers.Add<GzipCompressionProvider>();
    options.EnableForHttps = true;
});

// 세션 서비스 추가
builder.Services.AddSession(options =>
{
    options.Cookie.Name = ".YourApp.Session";
    options.IdleTimeout = TimeSpan.FromMinutes(20);
    options.Cookie.IsEssential = true; // GDPR 등의 이유로 필요
});

OpenAiControl.Initialize();

var credentials = new BasicAWSCredentials(AwsKey.accessKey, AwsKey.secretKey);
AwsKey.Client = new AmazonDynamoDBClient(credentials, RegionEndpoint.APNortheast1);
AwsKey.Context = new DynamoDBContext(AwsKey.Client);

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

app.UseRouting();

app.UseAuthorization();

app.UseSession();

app.UseResponseCompression();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Bedrock}/{id?}");

app.MapControllers();

app.Run();
