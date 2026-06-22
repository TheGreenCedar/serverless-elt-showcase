using Amazon.Lambda.Core;
using Amazon.SQS;
using Amazon.SQS.Model;
using TecFuelMix.Core;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace TecFuelMix.FetchLambda
{
    public sealed class Function
    {
        private static readonly Uri FuelMixUri = new("https://public-api.misoenergy.org/api/FuelMix");
        private static readonly TimeSpan TimeoutSafetyBuffer = TimeSpan.FromSeconds(1);
        private readonly HttpClient _httpClient;
        private readonly IAmazonSQS _sqs;
        private readonly string _queueUrl;

        public Function()
            : this(new HttpClient(), new AmazonSQSClient(), Environment.GetEnvironmentVariable("RAW_SNAPSHOT_QUEUE_URL") ?? "")
        {
        }

        public Function(HttpClient httpClient, IAmazonSQS sqs, string queueUrl)
        {
            _httpClient = httpClient;
            _sqs = sqs;
            _queueUrl = queueUrl;
        }

        public async Task Handler(object input, ILambdaContext context)
        {
            if (string.IsNullOrWhiteSpace(_queueUrl))
            {
                throw new InvalidOperationException("RAW_SNAPSHOT_QUEUE_URL is required.");
            }

            using var timeout = CreateInvocationTimeout(context);
            var cancellationToken = timeout.Token;
            using var response = await _httpClient.GetAsync(FuelMixUri, cancellationToken);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var snapshot = FuelMixParser.Parse(json);

            await _sqs.SendMessageAsync(new SendMessageRequest
            {
                QueueUrl = _queueUrl,
                MessageBody = json,
                MessageAttributes = new Dictionary<string, MessageAttributeValue>
                {
                    ["source_ref_id"] = new MessageAttributeValue { DataType = "String", StringValue = snapshot.SourceRefId },
                    ["fetched_at_utc"] = new MessageAttributeValue { DataType = "String", StringValue = DateTimeOffset.UtcNow.ToString("O") }
                }
            }, cancellationToken);

            context.Logger.LogInformation($"Queued MISO FuelMix snapshot {snapshot.SourceRefId}.");
        }

        private static CancellationTokenSource CreateInvocationTimeout(ILambdaContext context)
        {
            var timeout = new CancellationTokenSource();
            var remaining = context.RemainingTime;

            if (remaining <= TimeoutSafetyBuffer)
            {
                timeout.Cancel();
                return timeout;
            }

            timeout.CancelAfter(remaining - TimeoutSafetyBuffer);
            return timeout;
        }
    }
}
