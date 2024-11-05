using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
namespace Bedrock;

public static class AwsKey
{
    public static string accessKey = "AKIA6MLBOP6HQ3HH7XUC";
    public static string secretKey = "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";
    public static DynamoDBContext Context { get; set; }
    public static AmazonDynamoDBClient Client { get; set; }
}