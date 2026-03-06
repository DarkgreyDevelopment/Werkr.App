# Security Architecture

This document describes the security architecture of the Werkr platform — the cryptographic primitives, authentication and authorization schemes, secret storage strategy, and agent-side file path controls. For vulnerability reporting procedures, see [SECURITY.md](../SECURITY.md). For the overall system topology, see [Architecture](../Architecture.md).

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
5. The bundle record is stored with a `Pending` status and (up to) a 24-hour expiration window.

### Bundle Processing (Agent Side)

1. The admin pastes the bundle string and password into the Agent's localhost-only `/register` endpoint.
2. The Agent decrypts the bundle using the password, recovering the `BundleId`, `ServerUrl`, and the Server's RSA public key.
3. The Agent generates its own RSA 4096-bit keypair.
4. The Agent hybrid-encrypts its own public key with the Server's public key (from the bundle).
5. The Agent calls the API's `RegisterAgent` gRPC endpoint, sending:
   - The `BundleId` (plaintext correlation token — not a secret).
   - The hybrid-encrypted Agent public key.
   - The Agent's gRPC URL and machine name.
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

---

## Encrypted Envelope (gRPC Payload Encryption)

After registration, every gRPC payload between the API and Agent is wrapped in an `EncryptedEnvelope` protobuf message. This provides application-layer encryption on top of TLS.

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

The API can rotate the shared key via the `RotateSharedKey` RPC. During the grace period:

1. The decryptor checks the envelope's `key_id` against the **current** key first.
2. If the `key_id` does not match, it falls back to the **previous** key.
3. If neither matches, it attempts decryption with the current key (handles envelopes sent before key IDs were introduced).

This allows in-flight messages encrypted with the old key to be processed while new messages use the rotated key.

---

## Secret Storage

Each platform uses its native credential storage to protect sensitive material (database passphrases, keys).

| Platform | Backend | Storage Location |
|----------|---------|-----------------|
| Windows | DPAPI (CurrentUser scope) | `%LOCALAPPDATA%\Werkr\secrets\{key}.bin` |
| Linux | `secret-tool` (GNOME Keyring / Secret Service) | Fallback: `~/.config/werkr/secrets/` with `700`/`600` permissions |
| macOS | Keychain (`security` CLI) | Service name: `Werkr` |

`SecretStoreFactory` selects the correct implementation at runtime based on `RuntimeInformation.IsOSPlatform`.

### Agent Database Passphrase

The Agent's local database defaults to an encrypted SQLite database (PostgreSQL is also supported). The 32-byte hex passphrase is generated on first run and stored in the platform secret store under the key `werkr-agent-db`.

---

## Authentication

Werkr uses three authentication schemes depending on the caller.

### JWT Bearer Tokens

| Parameter | Value |
|-----------|-------|
| Algorithm | HMAC-SHA256 |
| Minimum signing key length | 32 characters |
| Default token lifetime | 15 minutes |
| Clock skew tolerance | 1 minute |
| Issuer | `werkr-api` |
| Audience | `werkr` |

Token claims include: `NameIdentifier`, `Role`, `Jti` (unique token ID), `ApiKeyId`, `ApiKeyName`, and one claim per granted permission.

JWT validation is configured centrally in `JwtValidationConfigurator` and shared by all components that need to validate tokens.

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

### API Keys

API keys provide non-interactive access for scripts and integrations.

| Parameter | Detail |
|-----------|--------|
| Format | `wk_` prefix + 32 random bytes (base64url-encoded) |
| Storage | SHA-512 hash of the key value (plaintext never stored) |
| Features | Expiration date, revocation, `LastUsedUtc` tracking |

### Auth Forwarding (Server to API)

When the Server needs to call the API on behalf of a user, `AuthForwardingHandler` mints a short-lived service JWT with full admin permissions and attaches it to outbound HTTP requests.

---

## Authorization (RBAC)

Authorization is permission-based, enforced via ASP.NET policy authorization.

### Roles and Permissions

| Role | Permissions |
|------|-------------|
| Admin | Create, Read, Update, Delete, Execute, Admin |
| Operator | Read, Execute |
| Viewer | Read |

### Policies

Each operation maps to a named policy:

| Policy | Required Permission |
|--------|-------------------|
| `CanCreate` | Create |
| `CanRead` | Read |
| `CanUpdate` | Update |
| `CanDelete` | Delete |
| `CanExecute` | Execute |
| `IsAdmin` | Admin |

Policies are enforced on both REST API endpoints and Blazor pages via `[Authorize]` attributes.

### Default Admin Account

On first startup, the identity seeder creates a default admin account:

| Property | Value |
|----------|-------|
| Email | `admin@werkr.local` |
| Password | Random 24 characters (guaranteed uppercase, lowercase, digit, symbol; Fisher-Yates shuffle) |
| `ChangePassword` | `true` (forced change on first login) |
| `Requires2FA` | `true` (TOTP enrollment required) |

The generated password is logged once at startup and never persisted.

### Password Policy

Aligned with NIST SP 800-63B:

- Minimum length: 12 characters.
- No character-class complexity requirements.
- Account lockout: 15 minutes after 5 failed attempts.

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

---

## Path Allowlisting (Agent)

The Agent validates all file and directory paths against a configurable allowlist before executing any file-system operation.

### Configuration

```json
{
  "Werkr": {
    "AllowedPaths": {
      "EnforceAllowlist": true,
      "Paths": [
        "C:\\Automation\\Scripts",
        "/opt/werkr/scripts"
      ]
    }
  }
}
```

Settings are bound via `IOptionsMonitor<AllowedPathsConfiguration>` and support hot-reload without restarting the Agent.

### Validation Rules

1. **Path normalization:** `Path.GetFullPath` resolves relative segments, followed by platform-specific steps:
   - **Windows:** 8.3 short-path expansion via `GetLongPathNameW` (P/Invoke), then symlink resolution and separator normalization.
   - **All platforms:** Reject paths containing `..` traversal after normalization.
2. **Dangerous path rejection (Windows):** Paths starting with `\\?\`, `\\.\`, or UNC `\\` prefixes are rejected, as are paths containing NTFS Alternate Data Streams (`:` after root).
3. **Prefix matching:** The normalized path must start with at least one entry in the configured `Paths` list. Comparison is ordinal-ignore-case on Windows, ordinal on Linux/macOS.
4. **Glob resolution:** When file patterns (wildcards) are used, each resolved file is validated individually. Symlinks that resolve outside the allowed paths are rejected (prevents symlink-through-glob attacks). Source and destination paths are checked to be distinct.

---

## Transport Security

| Concern | Configuration |
|---------|--------------|
| HTTP | Kestrel configured for `Http1AndHttp2`; HTTPS redirect and HSTS enabled in production |
| gRPC | HTTP/2 over TLS; ALPN negotiation selects the protocol automatically |
| Agent keepalive | gRPC ping interval: 30-second delay, 10-second timeout |
| TLS errors | Mapped to `CommandDispatchFailure.TlsError` for structured error handling |

### Data Protection

ASP.NET Data Protection keys are scoped to the application name `Werkr` and persisted to a `keys/` directory on disk via `PersistKeysToFileSystem`.

---

## Two-Factor Authentication (TOTP)

Werkr supports time-based one-time passwords (TOTP) for user accounts. MFA enrollment is enforced for the default admin account and can be required for any user by an administrator.

- **Enrollment:** Users scan a QR code or enter the shared secret manually in an authenticator app.
- **Verification:** A 6-digit TOTP code is required at login when 2FA is enabled.
- **Recovery:** Recovery codes are generated during enrollment for account recovery.
- **Admin enforcement:** Administrators can require 2FA for specific users; the cookie handler redirects unenrolled users to the setup page.
