# Vhaste15.CollaborativeEditor

[![NuGet](https://img.shields.io/nuget/v/Vhaste15.CollaborativeEditor.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/Vhaste15.CollaborativeEditor)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![Language](https://img.shields.io/badge/language-C%23-239120?style=flat-square&logo=csharp)](https://learn.microsoft.com/en-us/dotnet/csharp/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg?style=flat-square)](LICENSE)
[![GitHub package.json version](https://shields.io)](https://github.com/MRydex/angular-mrydex-editor)

Real-time collaborative document editing engine for **ASP.NET Core (.NET 10)** powered by **SignalR** and **Yjs (CRDT)**. Enables Google Docs-style multi-user editing, live cursor presence, seed locking, and optional database persistence.

---

## Table of Contents

- [Features](#features)
- [Architecture](#architecture)
- [Installation](#installation)
- [Quick Start (In-Memory Mode)](#quick-start-in-memory-mode)
- [Optional: Database Persistence](#optional-database-persistence)
- [Optional: Custom Permissions](#optional-custom-permissions)
- [Configuration](#configuration)
- [Hub & HTTP Contract](#hub--http-contract)
- [Contributing](#contributing)
- [License](#license)

---

## Features

- **CRDT-Based Sync (Yjs):** Zero-conflict concurrent text editing and offline reconciliation. The server acts as an opaque binary relay (`byte[]`) without parsing document contents.
- **Anonymous WebSocket Hub + Authenticated HTTP Join:** Connects through anonymous SignalR WebSockets while handling JWT/HTTP authentication, authorization, and room assignment via a standard REST endpoint (`POST /api/collaboration/join`).
- **Optional Persistence (`ICollaborativePersistence`):** Runs out of the box in pure in-memory relay mode with zero database dependencies, or plugs into your own database (SQL Server, PostgreSQL, etc.) by implementing a single interface.
- **Pluggable Permission Resolver (`ICollaborativePermissionResolver`):** Includes a default JWT claims resolver (`Admin` / `Viewer` / custom claims) that can be overridden with your own domain authorization logic.
- **Seed Locking & Compaction Support:** Prevents duplicate initial document seeding when multiple users open an existing HTML document simultaneously, and supports atomic history compaction.
- **Hot Toggle via `appsettings.json`:** Enable or disable collaboration at runtime without restarting the server using `IOptionsMonitor<CollaborationOptions>`.

---

## Architecture

```text
 Browser A ─┐                         ┌──────────────── API ────────────────┐
 Browser B ─┼── wss ── Proxy ───────▶ │ EditorHub (SignalR - Anonymous)     │
 Browser C ─┘   (optional)            │  ├─ Group per document              │
                                      │  ├─ CollaborationRooms (Memory)     │
 Browser ───┼── HTTPS (JWT) ────────▶ │  └─ CollaborationController (/join) │
                                      └──────────────────┬──────────────────┘
                                                         │ (Optional)
                                            ┌────────────┴────────────┐
                                            │ ICollaborativePersistence│
                                            └─────────────────────────┘
```

---

## Installation

Install the package via the .NET CLI:

```bash
dotnet add package Vhaste15.CollaborativeEditor
```

Or via the Package Manager Console:

```powershell
Install-Package Vhaste15.CollaborativeEditor
```

---

## Quick Start (In-Memory Mode)

By default, the library runs completely in memory, relaying Yjs deltas and cursor awareness packets between connected clients without requiring any database setup.

### Register Services & Endpoints in `Program.cs`

```csharp
using Vhaste15.CollaborativeEditor.Extensions;

var builder = WebApplication.CreateBuilder(args);

// 1. Register the collaborative editor engine
builder.Services.AddCollaborativeEditor(options =>
{
    options.Enabled = true;
    options.DocumentIdRegex = "^(contract|note)_[0-9]{1,10}$";
    options.KeepAliveIntervalSeconds = 15;
});

// 2. Configure CORS with credentials for your frontend client (e.g., Angular)
builder.Services.AddCors(options =>
{
    options.AddPolicy("ClientCors", policy =>
    {
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// 3. Configure your existing Authentication & Authorization
builder.Services.AddAuthentication().AddJwtBearer();
builder.Services.AddAuthorization();

var app = builder.Build();

app.UseCors("ClientCors");
app.UseWebSockets();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// 4. Map controllers (exposes POST /api/collaboration/join) and the anonymous SignalR Hub
app.MapControllers();
app.MapCollaborativeEditorHub("/hubs/editor");

app.Run();
```

> [!IMPORTANT]
> If your application sits behind a reverse proxy (YARP, Nginx, IIS ARR), ensure WebSocket upgrades are enabled and the Hub route (`/hubs/editor/{**catch-all}`) has higher routing priority than any SPA fallback.

---

## Optional: Database Persistence

If you want to persist Yjs binary updates and HTML snapshots to a database, implement `ICollaborativePersistence` and register it using `.AddCollaborativePersistence<T>()`.

### 1. Implement `ICollaborativePersistence`

```csharp
using Vhaste15.CollaborativeEditor.Abstractions;

public sealed class SqlCollaborativePersistence : ICollaborativePersistence
{
    public Task<IReadOnlyList<StoredUpdate>> GetUpdatesAsync(string documentId, CancellationToken ct)
    {
        // Return all stored binary updates ordered by Id ASC
        throw new NotImplementedException();
    }

    public Task AddUpdateAsync(string documentId, byte[] data, string userId, CancellationToken ct)
    {
        // Append a new binary delta row
        throw new NotImplementedException();
    }

    public Task CompactAsync(string documentId, IReadOnlyCollection<long> ids, byte[] state, string userId, CancellationToken ct)
    {
        // Atomically delete the specified row IDs and insert the compacted state blob
        throw new NotImplementedException();
    }

    public Task<string?> GetHtmlAsync(string documentId, CancellationToken ct)
    {
        // Return existing HTML if the document has never been opened in collaborative mode
        throw new NotImplementedException();
    }

    public Task SaveHtmlAsync(string documentId, string html, string userId, CancellationToken ct)
    {
        // Save periodic HTML snapshots for printing, indexing, or non-collaborative views
        throw new NotImplementedException();
    }
}
```

### 2. Register in `Program.cs`

```csharp
builder.Services
    .AddCollaborativeEditor()
    .AddCollaborativePersistence<SqlCollaborativePersistence>();
```

> [!NOTE]
> If an external process modifies a document's HTML outside the collaborative editor, delete its rows from your updates table so the next session triggers a fresh seed from the updated HTML.

---

## Optional: Custom Permissions

Out of the box, `JwtClaimPermissionResolver` checks the user's `ClaimsPrincipal` for roles (`Admin`, `Viewer`) or claims (`permission: editor:edit`, `doc_edit`, etc.).

To resolve permissions dynamically (e.g., checking document ownership or workflow state), implement `ICollaborativePermissionResolver`:

```csharp
using System.Security.Claims;
using Vhaste15.CollaborativeEditor.Abstractions;

public sealed class CustomDocumentPermissionResolver : ICollaborativePermissionResolver
{
    public Task<DocumentPermission> ResolveAsync(ClaimsPrincipal user, string documentId, CancellationToken ct)
    {
        // Return DocumentPermission.Edit, DocumentPermission.Read, or DocumentPermission.None
        return Task.FromResult(DocumentPermission.Edit);
    }
}
```

Register it **before** calling `AddCollaborativeEditor()`:

```csharp
builder.Services.AddScoped<ICollaborativePermissionResolver, CustomDocumentPermissionResolver>();
builder.Services.AddCollaborativeEditor();
```

---

## Configuration

Bind `CollaborationOptions` from `appsettings.json` (supports hot-reloading without server restarts):

```json
{
  "Collaboration": {
    "Enabled": true,
    "DocumentIdRegex": "^(contract|note)_[0-9]{1,10}$",
    "CompactionThreshold": 200,
    "MaxBytesUpdate": 1048576,
    "MaxBytesState": 2097152,
    "MaxBytesAwareness": 16384,
    "KeepAliveIntervalSeconds": 15
  }
}
```

```csharp
builder.Services.AddCollaborativeEditor(options =>
    builder.Configuration.GetSection("Collaboration").Bind(options));
```

| Option | Default | Description |
| :--- | :--- | :--- |
| `Enabled` | `true` | Toggles collaborative editing globally at runtime. |
| `DocumentIdRegex` | `^[a-zA-Z0-9_-]{1,100}$` | Strict validation pattern for incoming `documentId` values. |
| `CompactionThreshold` | `200` | Number of stored update rows before asking an editor client to compact history. |
| `MaxBytesUpdate` | `1048576` (1 MB) | Maximum allowed payload size for a single Yjs delta update. |
| `MaxBytesState` | `2097152` (2 MB) | Maximum allowed payload size for a compacted Yjs state and SignalR message size. |
| `MaxBytesAwareness` | `16384` (16 KB) | Maximum allowed payload size for cursor/selection awareness packets. |
| `KeepAliveIntervalSeconds` | `15` | SignalR WebSocket ping interval to prevent proxy idle timeouts. |

---

## Hub & HTTP Contract

1. **Connect WebSocket:** The client connects anonymously to `/hubs/editor` using WebSockets (`skipNegotiation: true`).
2. **HTTP Join (`POST /api/collaboration/join`):** The client sends `{ connectionId, documentId, clientId, initialHtml? }` with its standard `Authorization: Bearer <jwt>` header and receives `JoinResult`.
3. **Real-Time Sync:**
   - **Client $\rightarrow$ Server:** `SendUpdate(byte[])`, `SendAwareness(byte[])`, `Compact(byte[])`, `SaveHtml(string)`.
   - **Server $\rightarrow$ Client:** `ReceiveUpdate(byte[])`, `ReceiveAwareness(byte[])`, `PeerJoined(long)`, `PeerLeft(long)`, `SeedRequested(string)`.
4. **Reconnection:** On automatic SignalR reconnect, the client re-invokes `POST /api/collaboration/join` with its new `connectionId`.

---

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request or open an Issue for bugs and feature requests.

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add some amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

---

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.