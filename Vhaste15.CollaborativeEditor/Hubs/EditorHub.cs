using Vhaste15.CollaborativeEditor.Abstractions;
using Vhaste15.CollaborativeEditor.Internal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Text;

namespace Vhaste15.CollaborativeEditor.Hubs
{
    [AllowAnonymous]
    public sealed class EditorHub(
    CollaborationRooms rooms,
    IOptionsMonitor<CollaborationOptions> options,
    ICollaborativePersistence? persistence = null) : Hub<IEditorClient>
    {
        public override Task OnConnectedAsync()
        {
            if (!options.CurrentValue.Enabled)
            {
                Context.Abort(); //[cite: 5]
                return Task.CompletedTask;
            }
            return base.OnConnectedAsync();
        }

        public async Task SendUpdate(byte[] update)
        {
            var session = RequireSession(requireEdit: true);

            if (update.Length == 0 || update.Length > options.CurrentValue.MaxBytesUpdate)
                throw new HubException("Invalid change.");

            // Solo persiste si la app consumidora registró ICollaborativePersistence
            if (persistence is not null)
            {
                await persistence.AddUpdateAsync(session.DocumentId, update, session.UserId, CancellationToken.None);
            }

            rooms.SeedFinished(session.DocumentId);
            await Clients.OthersInGroup(session.DocumentId).ReceiveUpdate(update);
        }

        public Task SendAwareness(byte[] update)
        {
            var session = RequireSession(requireEdit: false);

            if (update.Length > options.CurrentValue.MaxBytesAwareness)
                throw new HubException("Invalid cursor.");

            return Clients.OthersInGroup(session.DocumentId).ReceiveAwareness(update);
        }

        public async Task Compact(byte[] state)
        {
            var session = RequireSession(requireEdit: true);

            if (persistence is null) return; // Sin BD no hay nada que compactar

            if (state.Length == 0 || state.Length > options.CurrentValue.MaxBytesState)
                throw new HubException("Invalid state.");

            long[] ids = rooms.ConsumeJoinedUpdateIds(Context.ConnectionId);
            if (ids.Length == 0) return;

            await persistence.CompactAsync(session.DocumentId, ids, state, session.UserId, CancellationToken.None);
        }

        public async Task SaveHtml(string html)
        {
            var session = RequireSession(requireEdit: true);

            if (persistence is not null)
            {
                await persistence.SaveHtmlAsync(session.DocumentId, html, session.UserId, CancellationToken.None);
            }
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            (string? documentId, long clientId, string? newOwner, string? pendingHtml) = rooms.Leave(Context.ConnectionId);

            if (documentId is not null)
            {
                if (newOwner is not null)
                {
                    string? html = persistence is not null
                        ? await persistence.GetHtmlAsync(documentId, CancellationToken.None)
                        : pendingHtml;

                    if (!string.IsNullOrEmpty(html))
                        await Clients.Client(newOwner).SeedRequested(html);
                }

                await Clients.OthersInGroup(documentId).PeerLeft(clientId);
            }

            await base.OnDisconnectedAsync(exception);
        }

        private CollaborationRooms.ConnectionSession RequireSession(bool requireEdit)
        {
            if (!options.CurrentValue.Enabled)
                throw new HubException("Collaborative editing is disabled.");

            var session = rooms.GetSession(Context.ConnectionId)
                ?? throw new HubException("You must join the document first.");

            rooms.AttachHubContext(Context.ConnectionId, Context);

            if (requireEdit && !session.CanEdit)
                throw new HubException("You do not have permission to edit.");

            return session;
        }
    }
}
