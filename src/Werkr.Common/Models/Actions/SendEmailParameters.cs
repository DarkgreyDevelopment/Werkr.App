namespace Werkr.Common.Models.Actions;

/// <summary>
/// Parameters for the <c>SendEmail</c> action. Sends an email via SMTP using MailKit.
/// Credentials are loaded from the <c>ISecretStore</c>
/// using the <see cref="CredentialName"/> key.
/// </summary>
public sealed record SendEmailParameters {

    /// <summary>SMTP server hostname.</summary>
    public required string SmtpHost { get; init; }

    /// <summary>SMTP server port. Defaults to 587 (submission/STARTTLS).</summary>
    public int Port { get; init; } = 587;

    /// <summary>Whether to use SSL/TLS. Defaults to <c>true</c>.</summary>
    public bool UseSsl { get; init; } = true;

    /// <summary>
    /// The key in the secret store that holds SMTP credentials.
    /// The secret value must be a JSON object: <c>{"username":"…","password":"…"}</c>.
    /// If omitted the connection is attempted without authentication.
    /// </summary>
    public string? CredentialName { get; init; }

    /// <summary>Sender email address.</summary>
    public required string From { get; init; }

    /// <summary>One or more recipient email addresses.</summary>
    public required string[] To { get; init; }

    /// <summary>Optional CC addresses.</summary>
    public string[]? Cc { get; init; }

    /// <summary>Email subject line.</summary>
    public required string Subject { get; init; }

    /// <summary>
    /// Email body text. If omitted the input variable value is used as the body.
    /// </summary>
    public string? Body { get; init; }

    /// <summary>Whether the body is HTML. Defaults to <c>false</c> (plain text).</summary>
    public bool IsHtml { get; init; }

    /// <summary>Optional file paths to attach. Resolved via <c>IFilePathResolver</c>.</summary>
    public string[]? Attachments { get; init; }
}
