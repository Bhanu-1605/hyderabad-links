namespace BusinessDirectory.Api.Notifications;

/// <summary>
/// One email, already rendered. Both bodies are always present: a plain-text part because some
/// people and most spam filters prefer it, and an HTML part because most mail clients show it.
/// </summary>
/// <param name="To">A single recipient. Nothing we send goes to more than one person at a time.</param>
public sealed record EmailMessage(string To, string Subject, string TextBody, string HtmlBody);

/// <summary>
/// Sends an email. The same shape as <see cref="Auth.ISmsSender"/>, for the same reason: the code
/// that wants to notify somebody should not know which provider is behind it, and in Development
/// there is no provider at all.
///
/// No provider implementation exists yet. Resend is the candidate (decision D14) but it has never
/// been configured or tested, and this project does not claim an integration works until it has been.
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// False when no provider is configured. Callers use it to skip work rather than to decide
    /// whether to record a notification: a notification that could not be sent is still worth a row.
    /// </summary>
    bool IsConfigured { get; }

    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>
/// Where email comes from and who a reply reaches. Nothing here has a production default: an unset
/// provider means email is simply not configured, which is honest.
/// </summary>
/// <param name="Provider">
/// Which provider to use ("console" in Development, "resend" once D14 is settled and tested).
/// </param>
/// <param name="ApiKey">
/// The provider's secret. It comes from Key Vault or an app setting, never from a committed file,
/// and it is never written to a log or an error message.
/// </param>
/// <param name="FromAddress">The envelope sender. Must be a domain the provider has verified.</param>
/// <param name="ReplyTo">
/// Where a reply should land when that is not the From address — an inbox a person actually reads.
/// </param>
/// <param name="SiteBaseUrl">
/// The public site, e.g. "https://hyderabadlinks.com". Email has no "current page", so a link in it
/// has to be absolute. Left unset, the templates leave the links out rather than printing a broken one.
/// </param>
public sealed record EmailSettings(
    string? Provider,
    string? ApiKey,
    string? FromAddress,
    string? ReplyTo,
    string? SiteBaseUrl)
{
    public static EmailSettings Load(IConfiguration configuration) => new(
        configuration["Email:Provider"],
        configuration["Email:ApiKey"],
        configuration["Email:FromAddress"],
        configuration["Email:ReplyTo"],
        configuration["Email:SiteBaseUrl"]);
}

/// <summary>
/// Development only: writes the whole message to the API console instead of sending it. This is how
/// the templates get checked on a laptop without a single real email leaving the machine.
/// </summary>
public sealed class ConsoleEmailSender(ILogger<ConsoleEmailSender> logger) : IEmailSender
{
    public bool IsConfigured => true;

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        // The plain-text body only: the HTML version says the same thing and would fill the console.
        logger.LogWarning(
            "DEVELOPMENT ONLY - email not sent. To: {To} | Subject: {Subject}\n{Body}",
            message.To, message.Subject, message.TextBody);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Used everywhere except Development until a real provider is configured. It throws rather than
/// returning quietly, so nothing can report that an owner was told something when nobody was.
/// The callers treat that throw as a delivery failure and record it (see <see cref="Notifier"/>).
/// </summary>
public sealed class UnconfiguredEmailSender : IEmailSender
{
    public bool IsConfigured => false;

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("No email provider is configured (Email:Provider).");
}
