using Vhaste15.CollaborativeEditor.Abstractions;
using Vhaste15.CollaborativeEditor.Hubs;
using Vhaste15.CollaborativeEditor.Internal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;

namespace Vhaste15.CollaborativeEditor.Controllers
{
    [ApiController]
    [Route("api/collaboration")]
    [Authorize]
    public sealed class CollaborationController(
    IHubContext<EditorHub, IEditorClient> hubContext,
    ICollaborativePermissionResolver permissionResolver,
    CollaborationRooms rooms,
    IOptionsMonitor<CollaborationOptions> options,
    ICollaborativePersistence? persistence = null) : ControllerBase
    {
        [HttpPost("join")]
        public async Task<ActionResult<JoinResult>> Join([FromBody] JoinRequest request, CancellationToken ct)
        {
            CollaborationOptions config = options.CurrentValue;
            if (!config.Enabled)
                return BadRequest("Collaborative editing is disabled.");

            if (string.IsNullOrWhiteSpace(request.ConnectionId))
                return BadRequest("ConnectionId is required.");

            if (rooms.IsConnected(request.ConnectionId))
                return Conflict("The connection is already in a document.");

            if (!Regex.IsMatch(request.DocumentId, config.DocumentIdRegex))
                return BadRequest("Invalid document format.");

            string? userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized("Invalid user token.");

            DocumentPermission permission = await permissionResolver.ResolveAsync(User, request.DocumentId, ct);
            if (permission == DocumentPermission.None)
                return Forbid();

            bool canEdit = permission == DocumentPermission.Edit;

            // 1. Suscribir socket al grupo antes de leer historial
            await hubContext.Groups.AddToGroupAsync(request.ConnectionId, request.DocumentId, ct);

            // 2. Si hay persistencia registrada, leer updates acumulados; si no, lista vacía
            IReadOnlyList<StoredUpdate> updates = persistence is not null
                ? await persistence.GetUpdatesAsync(request.DocumentId, ct)
                : [];

            var session = new CollaborationRooms.ConnectionSession
            {
                DocumentId = request.DocumentId,
                UserId = userId,
                ClientId = request.ClientId,
                CanEdit = canEdit,
                JoinedUpdateIds = updates.Select(u => u.Id).ToArray()
            };

            // 3. Si no hay updates, buscar HTML inicial en BD (si existe persistencia) o usar el enviado por el cliente
            string? htmlSource = updates.Count == 0
                ? (persistence is not null ? await persistence.GetHtmlAsync(request.DocumentId, ct) : request.InitialHtml)
                : null;

            var (seedHtml, seeding) = rooms.EnterAndResolveSeed(
                request.ConnectionId,
                session,
                htmlSource,
                hasExistingUpdates: updates.Count > 0);

            // 4. Notificar llegada a los demás editores
            await hubContext.Clients
                .GroupExcept(request.DocumentId, request.ConnectionId)
                .PeerJoined(request.ClientId);

            bool compact = persistence is not null && canEdit && updates.Count > config.CompactionThreshold;

            return Ok(new JoinResult(
                Updates: updates.Select(u => u.Data).ToList(),
                CanEdit: canEdit,
                SeedHtml: seedHtml,
                Seeding: seeding,
                Compact: compact));
        }
    }
}
