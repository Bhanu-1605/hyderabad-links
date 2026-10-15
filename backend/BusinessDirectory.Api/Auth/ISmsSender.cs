namespace BusinessDirectory.Api.Auth;

/// <summary>Sends the login code. The production implementation (MSG91, DLT-registered template) is added before launch.</summary>
public interface ISmsSender
{
    Task SendOtpAsync(string phoneE164, string code, CancellationToken cancellationToken);
}

/// <summary>Development only: writes the code to the API console instead of sending an SMS.</summary>
public sealed class ConsoleSmsSender(ILogger<ConsoleSmsSender> logger) : ISmsSender
{
    public Task SendOtpAsync(string phoneE164, string code, CancellationToken cancellationToken)
    {
        logger.LogWarning("DEVELOPMENT ONLY - login code for {Phone} is {Code}", phoneE164, code);
        return Task.CompletedTask;
    }
}

/// <summary>Used everywhere except Development until a real SMS provider is configured.</summary>
public sealed class UnconfiguredSmsSender : ISmsSender
{
    public Task SendOtpAsync(string phoneE164, string code, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("No SMS provider is configured.");
}
