# Security Architecture

This document describes the security architecture of the Werkr platform — the cryptographic primitives, authentication and authorization schemes, secret storage strategy, data protection controls, and agent-side security boundaries. For vulnerability reporting procedures, see [SECURITY.md](../SECURITY.md). For the overall system topology, see [Architecture](../Architecture.md).

---

## Cryptographic Primitives

All cryptographic operations are implemented in `EncryptionProvider` using the .NET `System.Security.Cryptography` APIs. No third-party cryptography libraries are used.

### Asymmetric Encryption (RSA)

| Parameter | Value |
|-----------|-------|
| Key size | 4096-bit (minimum enforced: 2048) |
| Encryption padding | OAEP with SHA-512 |
| Signature padding | PKCS#1 v1.5 with SHA-512 |

RSA keys are generated per component during registration. The public key is serialized as a JSON object containing `Modulus` and `Exponent` fields via `System.Text.Json`.

### Symmetric Encryption (AES-256-GCM)

| Parameter | Value |
|-----------|-------|
| Key length | 32 bytes (256-bit) |
| Nonce length | 12 bytes |
| Authentication tag | 16 bytes |

AES-256-GCM provides authenticated encryption — the tag ensures both confidentiality and integrity in a single pass.

### Hybrid Encryption

Hybrid encryption combines both schemes for encrypting arbitrary-length data to a public key:

1. Generate a random 32-byte AES key.
2. Encrypt the plaintext with AES-256-GCM.
3. Encrypt the AES key with the recipient's RSA public key (OAEP-SHA512).

**Wire format:**

```
┌────────────────────┬──────────┬──────────┬────────────┐
│ RSA-encrypted key  │  Nonce   │   Tag    │ Ciphertext │
│    (512 bytes)     │ (12 B)   │ (16 B)   │  (var)     │
└────────────────────┴──────────┴──────────┴────────────┘
```

Decryption reverses the process: RSA-decrypt the first 512 bytes to recover the AES key, then AES-GCM-decrypt the remainder.

### Password-Based Encryption

Used during registration bundle exchange, where no public key has been exchanged yet:

1. Derive a 32-byte key from the password by taking the first 32 bytes of its SHA-512 hash.
2. Encrypt with AES-256-GCM.

**Wire format:**

```
┌──────────┬──────────┬────────────┐
│  Nonce   │   Tag    │ Ciphertext │
│ (12 B)   │ (16 B)   │  (var)     │
└──────────┴──────────┴────────────┘
```

### Hashing

| Purpose | Algorithm |
|---------|-----------|
| Data integrity / general hashing | SHA-512 |
| Key fingerprints | SHA-512 |
| API key storage | SHA-512 |
| Token comparison | Constant-time via `CryptographicOperations.FixedTimeEquals` |

---

## Agent Registration

Registration establishes a trust relationship between the API and a new Agent. The flow uses a password-protected bundle exchanged out-of-band so that no unencrypted secrets traverse the network.

### Bundle Creation (API Side)

1. The admin triggers registration in the Server UI, which calls the API.
2. The API generates:
   - A 16-byte random correlation token (`BundleId`).
   - The API's RSA public key bytes.
   - A `RegistrationBundlePayload` containing the `BundleId`, `ConnectionName`, `ServerUrl`, and `ServerPublicKeyBytes`.
3. The payload is encrypted with the admin-supplied password (password-based AES-256-GCM) and Base64-encoded.
4. The resulting bundle string is displayed to the admin for transfer.
5. The bundle record is stored with a `Pending` status and a configurable expiration window (up to 24 hours).

### Bundle Processing (Agent Side)

1. The admin pastes the bundle string and password into the Agent's localhost-only `/register` endpoint.
2. The Agent decrypts the bundle using the password, recovering the `BundleId`, `ServerUrl`, and the Server's RSA public key.
3. The Agent generates its own RSA 4096-bit keypair.
4. The Agent hybrid-encrypts its own public key with the Server's public key (from the bundle).
5. The Agent calls the API's `RegisterAgent` gRPC endpoint. All registration fields (agent URL, name, bundle ID, public key) are protected in a single encrypted envelope. A non-secret hash-based lookup prevents leaking registration data during the correlation step.
6. The API looks up the bundle by `BundleId`, verifies it is `Pending` and not expired, then hybrid-decrypts the Agent's public key using the Server's private key (stored with the bundle record).
7. The API generates:
   - Two 64-byte random API keys (Agent-to-API and API-to-Agent), encoded as 128-character hex strings.
   - A 32-byte `SharedKey` for envelope encryption.
8. The API stores the Agent-to-API key as a SHA-512 hash (never plaintext) and the API-to-Agent key in plaintext for outbound use.
9. The API returns a `RegistrationResponsePayload` (containing both API keys, the `SharedKey`, and a `ConnectionId`), hybrid-encrypted to the Agent's public key.
10. The Agent hybrid-decrypts the response with its own private key and persists the connection record to its local database.

### Bundle Expiration

A background service (`BundleExpirationService`) runs on a 1-hour interval, transitioning any `Pending` bundles past their `ExpiresAt` timestamp to `Expired` status.

### Platform Validation

On startup, the Agent performs a fail-fast RSA OAEP-SHA512 round-trip test to verify that the platform's cryptographic provider supports the required algorithms.

### Agent Management

- Each agent receives a unique, system-generated tag (`agent:{agent-id}`) at registration time. This tag is non-editable and non-deletable, enabling precise single-agent targeting.
- Agent heartbeat interval: 30 seconds (configurable). An agent is considered offline after 3 consecutive missed heartbeats (90 seconds, configurable).
- Agents report their capabilities (supported task types, installed action handlers, OS platform, architecture, agent version) during registration and via periodic heartbeat. The API validates capabilities before dispatching work.
- Agents below the minimum compatible version are rejected at registration with a descriptive error.
- Deregistration revokes the agent's keys and cleans up references. All registration and deregistration events are audit-logged.

---

## Encrypted Envelope (gRPC Payload Encryption)

After registration, every gRPC payload between the API and Agent is wrapped in an `EncryptedEnvelope` protobuf message. This provides application-layer encryption on top of TLS. The envelope supports arbitrary inner payload types, enabling new gRPC services to use the same encryption without modifying the envelope contract.

### Envelope Structure

```protobuf
message EncryptedEnvelope {
  bytes  ciphertext = 1;  // AES-256-GCM encrypted payload
  bytes  iv         = 2;  // 12-byte nonce
  bytes  auth_tag   = 3;  // 16-byte GCM authentication tag
  string key_id     = 4;  // Identifies which shared key was used
}
```

### Encrypt / Decrypt Flow

**Encrypt:** Serialize the inner protobuf message to bytes, encrypt with AES-256-GCM using the shared key, and populate the envelope fields.

**Decrypt:** Read the `key_id` to select the correct shared key, AES-GCM-decrypt the `ciphertext` using the `iv` and `auth_tag`, then deserialize the inner protobuf.

### Key Rotation

The API can rotate the shared key via the `RotateSharedKey` RPC. During the configurable grace period (default: 5 minutes), both the current and previous keys are valid to avoid disrupting in-flight messages:

1. The decryptor checks the envelope's `key_id` against the **current** key first.
2. If the `key_id` does not match, it falls back to the **previous** key.
3. If neither matches, it attempts decryption with the current key (handles envelopes sent before key IDs were introduced).

After the grace period, the previous key is invalidated. Key rotation events are audit-logged.

### Key Rotation Failure Modes

- **Unreachable agent** — if an agent is unreachable during key rotation, the API retains the current key and retries rotation on the next successful heartbeat.
- **Expired key** — if an agent presents an expired key after the grace period, the API rejects the request and the agent must re-register.
- **Envelope version mismatch** — if an agent uses an older envelope format, the request is rejected with a descriptive error; the agent logs the failure and attempts reconnection with the current envelope version.
- All key rotation failures are audit-logged.

---

## Secret Storage

Each platform uses its native credential storage to protect sensitive material (database passphrases, keys).

| Platform | Backend | Storage Location |
|----------|---------|-----------------|
| Windows | DPAPI (CurrentUser scope) | `%LOCALAPPDATA%\Werkr\secrets\{key}.bin` |
| Linux | Protected file (owner-only read, mode 0600) | `/etc/werkr/keys/` |
| macOS | Keychain (`security` CLI) | Service name: `Werkr` |

`SecretStoreFactory` selects the correct implementation at runtime based on `RuntimeInformation.IsOSPlatform`.

### Agent Database Passphrase

The Agent's local database defaults to an encrypted SQLite database (PostgreSQL is also supported). The 32-byte hex passphrase is generated on first run and stored in the platform secret store under the key `werkr-agent-db`.

---

## Database Encryption at Rest

Transparent column-level AES-256-GCM encryption protects sensitive data stored in the application database:

- **Encrypted fields** — credentials, workflow variable values, connection strings, and API key hashes.
- **Key management** — platform-appropriate key storage (DPAPI on Windows, Keychain on macOS, protected file on Linux), consistent with the secret storage model described above.
- **Key rotation** — zero-downtime re-encryption: a new key is introduced, data is re-encrypted in background batches, and the old key is retired after all records are migrated.
- **Migration tool** — a separate tool is provided for encrypting existing unencrypted data on upgrade from pre-encryption versions.

---

## Authentication

Werkr uses multiple authentication schemes depending on the caller and context.

### JWT Bearer Tokens

| Parameter | Value |
|-----------|-------|
| Algorithm | HMAC-SHA256 |
| Minimum signing key length | 32 characters |
| Default token lifetime | 15 minutes (configurable) |
| Clock skew tolerance | 1 minute |
| Issuer | `werkr-api` |
| Audience | `werkr` |

Token claims include: `NameIdentifier`, `Role`, `Jti` (unique token ID), `ApiKeyId`, `ApiKeyName`, and one claim per granted permission.

JWT validation is configured centrally in `JwtValidationConfigurator` and shared by all components that need to validate tokens. JWTs are used for browser-session-originated requests forwarded by the Server. Werkr.Server manages token renewal for browser sessions via sliding expiration.

### Cookie Authentication

Used for interactive browser sessions on the Server.

| Parameter | Value |
|-----------|-------|
| Sliding expiration | 30 minutes |
| Cookie flags | `HttpOnly`, `SameSite=Strict`, `SecurePolicy=SameAsRequest` |

The cookie authentication handler enforces additional checks:

- Rejects requests from disabled user accounts.
- Redirects users who must change their password.
- Redirects users who have not completed 2FA enrollment when required.

### Passkey Support (WebAuthn/FIDO2)

WebAuthn/FIDO2 passkeys are supported as both a primary authentication method (passwordless) and as an optional second-factor method:

- Users can register one or more passkeys alongside or instead of TOTP.
- Passkey authentication satisfies the platform's 2FA requirement when used as the primary method (the passkey itself provides multi-factor assurance via the authenticator's user verification).
- Passkey registration, authentication, and removal events are audit-logged.

### Login Rate Limiting

Per-IP rate limits are enforced on authentication endpoints to mitigate credential stuffing and brute-force attacks. This operates independently of per-account lockout (see Password Policy below) — both mechanisms are evaluated, and the most restrictive applies.

---

## API Keys

API keys provide non-interactive, programmatic access for CI/CD pipelines, external integrations, and automation.

### Key Format and Storage

| Parameter | Detail |
|-----------|--------|
| Format | `wk_` prefix + 32 random bytes (base64url-encoded) |
| Storage | SHA-512 hash of the key value (plaintext never stored) |
| Display | Keys are displayed once at creation and cannot be retrieved afterward |

### Key Lifecycle

- **Create** — via the UI and REST API. At creation, the user selects which of their permissions the key carries; all permissions are selected by default.
- **Revoke** — immediate invalidation of a key.
- **Rotate** — create a new key and revoke the old one in a single operation.
- **Expiration** — configurable expiration dates. `LastUsedUtc` timestamp tracking.

### Permission Scoping

- Keys cannot exceed the creator's current permissions at creation time.
- If the creator's permissions are subsequently **reduced** (role demotion or permission removal), all active API keys for that user are fully revoked. The user must create new API keys after their permissions change.
- Permission **additions** to the creator's role do not retroactively expand existing keys or require key recreation.

### Rate Limiting

- Per-key rate limits are configurable.
- There are no concurrency limits on simultaneous use of the same API key from multiple clients.
- API trigger rate limits apply independently of API key rate limits; both limits are evaluated and the most restrictive applies.

### Audit Logging

All key creation, revocation, rotation, and usage events are recorded in the audit log.

---

## Auth Forwarding & Service Identity

### User-Scoped API Forwarding

API calls originating from the UI carry the authenticated user's identity, role, and permissions. UI actions are authorized at the user's permission level, not an elevated service account. This ensures that a user cannot perform actions through the UI that exceed their granted permissions. Background server-initiated operations (health monitoring) use a separate administrative channel.

### System Service Identity

Trigger-initiated workflow execution (schedule, file monitor, workflow completion, API triggers) uses a system service identity. Trigger configuration requires elevated permissions, which gates what workflows can be auto-triggered. The system service identity is distinct from any user account and is used solely for automated operations.

---

## Authorization (RBAC)

Authorization is permission-based, enforced via ASP.NET policy authorization. Every API endpoint and UI page is protected by permission-based policies rather than fixed role checks.

### Permission Model

Permissions use a hierarchical `resource:action` naming convention (e.g., `workflows:execute`, `agents:manage`, `settings:write`, `views:create`, `views:share`). Permissions are organized under their owning domain namespace. All registered permissions appear in the role management UI.

Permissions are registered at application startup. The permission model supports additive evolution — new permissions can be introduced without modifying existing permission definitions.

### Custom Roles

Administrators create custom roles and assign fine-grained permissions. The role management UI provides a matrix interface for permission assignment and user-to-role mapping.

### Built-in Roles

Three non-deletable default roles ship with predefined permission sets:

| Role | Permissions |
|------|-------------|
| **Admin** | All permissions |
| **Operator** | Create, read, update, execute operations |
| **Viewer** | Read-only access |

### Per-Workflow Execution Permissions

Roles may be granted or denied execution permission on specific workflows, providing granular control over who can trigger which automations.

### Policies

Policy-based authorization is enforced on both REST API endpoints and Blazor pages via `[Authorize]` attributes. Policies reference the hierarchical permission model rather than fixed role names.

### Default Admin Account

On first startup, the identity seeder creates a default admin account:

| Property | Value |
|----------|-------|
| Email | `admin@werkr.local` |
| Password | Random 24 characters (guaranteed uppercase, lowercase, digit, symbol; Fisher-Yates shuffle) |
| `ChangePassword` | `true` (forced change on first login) |
| `Requires2FA` | `true` (TOTP enrollment required) |

The generated password is logged once at startup and never persisted.

---

## User Management

### User Lifecycle

- **Invitation** — administrators create user accounts with initial role assignments.
- **Deactivation** — suspend a user without deleting their account or audit history. Deactivated users cannot authenticate.
- **Password reset** — self-service forgot-password flow via email.

### Session Management

- Administrators can view and revoke active user sessions.
- Revoked sessions are invalidated immediately; the affected user is required to re-authenticate.
- Default maximum session count per user: 5. When exceeded, the oldest session is automatically revoked.

### Audit Logging

User lifecycle events (creation, deactivation, role changes) and session events (login, logout, revocation) are audit-logged.

---

## Password Policy

Aligned with NIST SP 800-63B §5.1.1.2:

- **Minimum length** — 12 characters.
- **No character-class complexity requirements** — no mandatory uppercase, lowercase, digit, or symbol rules.
- **Password history** — enforcement of 5 previous passwords (configurable). Users cannot reuse recent passwords.
- **Account lockout** — 15 minutes after 5 failed attempts.

---

## Two-Factor Authentication

### TOTP (Time-Based One-Time Passwords)

Werkr supports TOTP for user accounts. MFA enrollment is enforced for the default admin account and can be required for any user or role by an administrator.

- **Enrollment** — users scan a QR code or enter the shared secret manually in an authenticator app.
- **Verification** — a 6-digit TOTP code is required at login when 2FA is enabled.
- **Recovery codes** — 10 single-use recovery codes are generated during enrollment for account recovery. Users may regenerate codes at any time, which invalidates all previous codes.
- **Admin enforcement** — administrators can require 2FA enrollment for all users or specific roles. The cookie handler redirects unenrolled users to the setup page.

### Passkeys

WebAuthn/FIDO2 passkeys serve as an alternative or complement to TOTP. See the Authentication section above for details.

---

## gRPC Agent Authentication

Agent-to-API and API-to-Agent gRPC calls are authenticated using the API keys established during registration.

### Verification Flow

1. The caller attaches the raw API key as a bearer token and its `ConnectionId` as the `x-werkr-connection-id` metadata header.
2. The interceptor (`AgentBearerTokenInterceptor` on the API, `BearerTokenInterceptor` on the Agent) looks up the `RegisteredConnection` by `ConnectionId`.
3. The interceptor computes the SHA-512 hash of the presented token and compares it to the stored hash using `CryptographicOperations.FixedTimeEquals` (constant-time comparison to prevent timing attacks).
4. On success, the `RegisteredConnection` is stored in `context.UserState` for downstream service methods.
5. `LastSeen` is updated with a 60-second debounce threshold to avoid database writes on every call.

### Distinction Between Sides

An `IsServer` flag on the `RegisteredConnection` record distinguishes the API-held record (where `IsServer = true` and the Agent-to-API key hash is stored) from the Agent-held record (where `IsServer = false` and the API-to-Agent key hash is stored).

### Agent Offline Mid-Job

When an agent becomes unreachable mid-job, the API considers the agent's in-flight jobs as still running. Jobs transition to failed when the first of the following thresholds is exceeded:

1. Task maximum run duration.
2. Agent heartbeat timeout (3 consecutive missed heartbeats).
3. Workflow-level timeout.

---

## Path Allowlisting (Agent)

The Agent validates all file and directory paths against a configurable allowlist before executing any file-system operation.

### Configuration

Path allowlists are configured per-agent through the agent settings UI. Each agent's allowlist defines which filesystem paths the agent is permitted to access during task execution. The default posture is **deny-all** — agents with an empty allowlist cannot access any filesystem paths.

The allowlist supports standard glob patterns (`*` for multiple character wildcards and `?` for single character wildcards).

Allowlist changes are audit-logged and distributed to the agent via the encrypted gRPC configuration synchronization channel.

### Validation Rules

1. **Path normalization:** `Path.GetFullPath` resolves relative segments, followed by platform-specific steps:
   - **Windows:** 8.3 short-path expansion via `GetLongPathNameW` (P/Invoke), then symlink resolution and separator normalization.
   - **All platforms:** Reject paths containing `..` traversal after normalization.
2. **Dangerous path rejection (Windows):** Paths starting with `\\?\`, `\\.\`, or UNC `\\` prefixes are rejected, as are paths containing NTFS Alternate Data Streams (`:` after root).
3. **Prefix matching:** The normalized path must start with at least one entry in the configured paths list. Comparison is ordinal-ignore-case on Windows, ordinal on Linux/macOS.
4. **Glob resolution:** When file patterns (wildcards) are used, each resolved file is validated individually. Symlinks that resolve outside the allowed paths are rejected (prevents symlink-through-glob attacks). Source and destination paths are checked to be distinct.

---

## Outbound Request Controls

The HTTP Request, Send Webhook, and File Download/Upload action handlers validate target URLs against configurable security controls.

### URL Allowlisting

Requests to URLs not on the configured allowlist are rejected. The allowlist is configured at the platform level.

### Private Network Protection

Requests to private and internal IP ranges (RFC 1918, link-local, loopback) are blocked by default. An explicit override is required to permit internal network targets. This prevents server-side request forgery (SSRF) attacks against internal infrastructure.

### DNS Rebinding Protection

Resolved IP addresses are validated against the allowlist **after** DNS resolution. This prevents DNS rebinding attacks where a domain initially resolves to an allowed IP and then re-resolves to a private address during the request.

---

## File Monitoring Security

File monitor triggers (persistent triggers that watch directories for file events) enforce the following security controls:

- **Path validation** — monitored paths must fall within the agent's configured path allowlist.
- **Canonical path resolution** — prevents symbolic link and directory traversal attacks.
- **Debounce** — configurable debounce window (default: 500 ms) prevents trigger flooding from rapid file system events.
- **Circuit breaker** — excessive trigger rates trip a circuit breaker to prevent resource exhaustion.
- **Watch limit** — configurable maximum watch count per agent (default: 50) prevents resource exhaustion from excessive file monitors.
- **Elevated permissions** — trigger configuration requires elevated permissions. All trigger configuration changes are audit-logged.

---

## API Trigger Security

API triggers (REST endpoints that initiate workflow runs) enforce the following security controls:

- **Authentication** — API triggers require authentication via API key or bearer token. The workflow ID is specified in the request body or URL parameter.
- **Rate limiting** — configurable per-workflow rate limits apply independently of API key rate limits. Both limits are evaluated and the most restrictive applies. Rate-limited callers receive an HTTP 429 response with a `Retry-After` header indicating when the next request will be accepted.
- **Request validation** — optional JSON schema validation for trigger payloads.
- **Payload injection** — validated trigger payloads are injected as workflow input variables.
- **Cycle detection** — the trigger registry detects circular workflow-completion chains at configuration time and surfaces a prominent warning in the workflow list and workflow editor UI. Circular chains are not blocked (users may intentionally create cyclical workflows). Workflow-completion trigger chains have a configurable maximum chain depth (default: 5). Each trigger-initiated run carries a chain depth counter; when max depth is reached, the trigger is suppressed with an audit log entry. Manual triggers reset the counter to 0.

---

## Transport Security

| Concern | Configuration |
|---------|--------------|
| HTTP | Kestrel configured for `Http1AndHttp2`; HTTPS redirect and HSTS enabled in production |
| gRPC | HTTP/2 over TLS; ALPN negotiation selects the protocol automatically |
| Agent keepalive | gRPC ping interval: 30-second delay, 10-second timeout |
| TLS errors | Mapped to `CommandDispatchFailure.TlsError` for structured error handling |

### TLS Enforcement

All connections (browser to Server, Server to API, API to Agent) require HTTPS/TLS. URL scheme validation is enforced at registration, notification channel creation, and gRPC channel construction. HTTP URLs are explicitly rejected.

### Data Protection

ASP.NET Data Protection keys are scoped to the application name `Werkr` and persisted to a `keys/` directory on disk via `PersistKeysToFileSystem`.

---

## Content Security Policy

The Blazor Server UI enforces Content Security Policy (CSP) headers with directives appropriate for:

- Blazor Server rendering (inline scripts and styles required by the framework).
- SignalR WebSocket connections for real-time updates.
- JavaScript interop for the DAG editor graph library.

CSP directives are configured to balance security (preventing XSS and data injection) with the functional requirements of the Blazor Server architecture.

---

## Sensitive Data Redaction

Configurable mechanisms prevent sensitive data from appearing in execution logs, output previews, and real-time log streaming.

### Variable-Level Redaction

Workflow variables can be flagged as "redact from logs." Variables with this flag have their resolved values automatically replaced with `[REDACTED]` in all execution output. Variable-level redaction is applied first, before regex-based pattern matching.

### Regex-Based Redaction

Configurable regex patterns automatically mask sensitive data (passwords, tokens, connection strings, API keys) in execution log output:

- Default redaction patterns ship with the platform. Administrators can add custom patterns.
- Custom regex patterns are validated at save time. Patterns that fail compilation or exceed a complexity threshold (default: 1 second compilation time) are rejected.
- Redacted values are replaced with a consistent `[REDACTED]` marker.

### Redaction Order

1. Variable-level redaction flags are applied first.
2. Regex-based patterns are applied afterward to catch any remaining sensitive values not covered by explicit flags.

Redaction applies to stored output, output previews, and real-time log streaming.

---

## Variable Escaping

Workflow variables are escaped or encoded appropriately for the receiving execution context before interpolation to prevent injection attacks:

- **Shell commands** — variables are escaped according to the target shell's quoting rules (e.g., `cmd.exe` on Windows, `/bin/sh` on Linux/macOS).
- **Action handler parameters** — values are encoded appropriately for the target context (e.g., file paths, HTTP headers).
- **Discrete argument passing** — where the execution model supports it (PowerShell parameters, process arguments), variables are passed as discrete arguments rather than interpolated into command strings, eliminating injection risk entirely.

---

## Compliance Alignment

The security architecture aligns with:

- **OWASP Top 10** — mitigations for injection, broken authentication, sensitive data exposure, XML external entities, broken access control, security misconfiguration, XSS, insecure deserialization, insufficient logging and monitoring, and SSRF.
- **NIST SP 800-63B** — authentication guidelines including password policy (§5.1.1.2), multi-factor authentication, and session management.

Specific compliance mapping is maintained in the security documentation.
