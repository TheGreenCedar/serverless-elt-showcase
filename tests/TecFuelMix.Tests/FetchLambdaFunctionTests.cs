using Amazon.SQS;
using Amazon.SQS.Model;
using System.Net;
using TecFuelMix.FetchLambda;

namespace TecFuelMix.Tests;

public sealed class FetchLambdaFunctionTests
{
    [Fact]
    public async Task Handler_sends_raw_payload_to_sqs_with_snapshot_attributes()
    {
        using var httpClient = new HttpClient(new StaticHttpMessageHandler(SamplePayloads.FuelMixJson));
        var sqs = new RecordingSqsClient();
        var context = new TestLambdaContext(TimeSpan.FromSeconds(10));
        var function = new Function(httpClient, sqs, "https://sqs.us-east-1.amazonaws.com/123/raw");

        await function.Handler(new object(), context);

        Assert.NotNull(sqs.Request);
        Assert.Equal("https://sqs.us-east-1.amazonaws.com/123/raw", sqs.Request.QueueUrl);
        Assert.Equal(SamplePayloads.FuelMixJson, sqs.Request.MessageBody);
        Assert.Equal("22-Jun-2026 - Interval 11:05 EST", sqs.Request.MessageAttributes["source_ref_id"].StringValue);
        Assert.Equal("String", sqs.Request.MessageAttributes["source_ref_id"].DataType);
        Assert.Equal("String", sqs.Request.MessageAttributes["fetched_at_utc"].DataType);
        Assert.True(DateTimeOffset.TryParse(sqs.Request.MessageAttributes["fetched_at_utc"].StringValue, out _));
        Assert.True(sqs.CancellationToken.CanBeCanceled);
    }

    [Fact]
    public async Task Handler_missing_queue_url_throws_without_publishing()
    {
        using var httpClient = new HttpClient(new StaticHttpMessageHandler(SamplePayloads.FuelMixJson));
        var sqs = new RecordingSqsClient();
        var context = new TestLambdaContext(TimeSpan.FromSeconds(10));
        var function = new Function(httpClient, sqs, "");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => function.Handler(new object(), context));

        Assert.Equal("RAW_SNAPSHOT_QUEUE_URL is required.", ex.Message);
        Assert.Null(sqs.Request);
    }

    [Fact]
    public async Task Handler_http_failure_throws_without_publishing()
    {
        using var httpClient = new HttpClient(new StaticHttpMessageHandler("unavailable", HttpStatusCode.InternalServerError));
        var sqs = new RecordingSqsClient();
        var context = new TestLambdaContext(TimeSpan.FromSeconds(10));
        var function = new Function(httpClient, sqs, "https://sqs.us-east-1.amazonaws.com/123/raw");

        await Assert.ThrowsAsync<HttpRequestException>(() => function.Handler(new object(), context));

        Assert.Null(sqs.Request);
    }

    private sealed class StaticHttpMessageHandler(string body, HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body)
            };

            return Task.FromResult(response);
        }
    }

    private sealed class RecordingSqsClient : AmazonSQSClient
    {
        public SendMessageRequest? Request { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public override Task<SendMessageResponse> SendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            CancellationToken = cancellationToken;
            return Task.FromResult(new SendMessageResponse { MessageId = "message-1" });
        }
    }
}
