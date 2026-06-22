using Amazon.Lambda.Core;

namespace TecFuelMix.Tests;

internal sealed class TestLambdaContext(TimeSpan remainingTime) : ILambdaContext
{
    public string AwsRequestId => "request-1";
    public IClientContext ClientContext => throw new NotSupportedException();
    public string FunctionName => "TecFuelMix.Tests";
    public string FunctionVersion => "1";
    public ICognitoIdentity Identity => throw new NotSupportedException();
    public string InvokedFunctionArn => "arn:aws:lambda:us-east-1:123:function:TecFuelMix.Tests";
    public ILambdaLogger Logger { get; } = new TestLambdaLogger();
    public string LogGroupName => "/aws/lambda/TecFuelMix.Tests";
    public string LogStreamName => "stream";
    public int MemoryLimitInMB => 256;
    public TimeSpan RemainingTime { get; } = remainingTime;
    public string TenantId => "";
    public string TraceId => "trace-1";
    public ILambdaSerializer Serializer => throw new NotSupportedException();
}

internal sealed class TestLambdaLogger : ILambdaLogger
{
    public void Log(string message)
    {
    }

    public void LogLine(string message)
    {
    }

    public void Log(string level, string message)
    {
    }

    public void Log(LogLevel level, string message)
    {
    }

    public void LogTrace(string message)
    {
    }

    public void LogDebug(string message)
    {
    }

    public void LogInformation(string message)
    {
    }

    public void LogWarning(string message)
    {
    }

    public void LogError(string message)
    {
    }

    public void LogCritical(string message)
    {
    }

    public void Log(string level, string message, params object[] args)
    {
    }

    public void Log(string level, Exception exception, string message, params object[] args)
    {
    }

    public void Log(LogLevel level, string message, params object[] args)
    {
    }

    public void Log(LogLevel level, Exception exception, string message, params object[] args)
    {
    }

    public void LogTrace(string message, params object[] args)
    {
    }

    public void LogTrace(Exception exception, string message, params object[] args)
    {
    }

    public void LogDebug(string message, params object[] args)
    {
    }

    public void LogDebug(Exception exception, string message, params object[] args)
    {
    }

    public void LogInformation(string message, params object[] args)
    {
    }

    public void LogInformation(Exception exception, string message, params object[] args)
    {
    }

    public void LogWarning(string message, params object[] args)
    {
    }

    public void LogWarning(Exception exception, string message, params object[] args)
    {
    }

    public void LogError(string message, params object[] args)
    {
    }

    public void LogError(Exception exception, string message, params object[] args)
    {
    }

    public void LogCritical(string message, params object[] args)
    {
    }

    public void LogCritical(Exception exception, string message, params object[] args)
    {
    }
}
