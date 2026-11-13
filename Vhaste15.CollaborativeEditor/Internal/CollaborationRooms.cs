using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.Text;

namespace Vhaste15.CollaborativeEditor.Internal
{
    public sealed class CollaborationRooms
    {
        public sealed class ConnectionSession
        {
            public required string DocumentId { get; init; }
            public required string UserId { get; init; }
            public required long ClientId { get; init; }
            public required bool CanEdit { get; init; }
            public long[] JoinedUpdateIds { get; set; } = [];
            public HubCallerContext? HubContext { get; set; }
        }

        private sealed class Room
        {
            public Dictionary<string, ConnectionSession> Members { get; } = [];
            public string? SeedOwner { get; set; }
            public bool IsSeeded { get; set; }
            public string? InMemoryInitialHtml { get; set; }
        }

        private readonly Dictionary<string, Room> _rooms = [];
        private readonly Dictionary<string, ConnectionSession> _sessionsByConnection = [];
        private readonly Lock _lock = new();

        public bool IsConnected(string connectionId)
        {
            lock (_lock) return _sessionsByConnection.ContainsKey(connectionId);
        }

        public ConnectionSession? GetSession(string connectionId)
        {
            lock (_lock) return _sessionsByConnection.GetValueOrDefault(connectionId);
        }

        public void AttachHubContext(string connectionId, HubCallerContext context)
        {
            lock (_lock)
            {
                if (_sessionsByConnection.TryGetValue(connectionId, out var session))
                    session.HubContext = context;
            }
        }

        public (string? SeedHtml, bool Seeding) EnterAndResolveSeed(
            string connectionId,
            ConnectionSession session,
            string? htmlSource,
            bool hasExistingUpdates)
        {
            lock (_lock)
            {
                Room room = GetRoom(session.DocumentId);
                room.Members[connectionId] = session;
                _sessionsByConnection[connectionId] = session;

                if (hasExistingUpdates)
                {
                    room.IsSeeded = true;
                    return (null, false);
                }

                if (!string.IsNullOrEmpty(htmlSource) && room.InMemoryInitialHtml is null)
                    room.InMemoryInitialHtml = htmlSource;

                if (room.IsSeeded || string.IsNullOrEmpty(room.InMemoryInitialHtml))
                    return (null, false);

                bool lockFree = room.SeedOwner is null || !room.Members.ContainsKey(room.SeedOwner);
                if (session.CanEdit && lockFree)
                {
                    room.SeedOwner = connectionId;
                    return (room.InMemoryInitialHtml, false);
                }

                return (null, true);
            }
        }

        public void SeedFinished(string documentId)
        {
            lock (_lock)
            {
                if (_rooms.TryGetValue(documentId, out Room? room))
                {
                    room.SeedOwner = null;
                    room.IsSeeded = true;
                }
            }
        }

        public long[] ConsumeJoinedUpdateIds(string connectionId)
        {
            lock (_lock)
            {
                if (!_sessionsByConnection.TryGetValue(connectionId, out var session))
                    return [];

                long[] ids = session.JoinedUpdateIds;
                session.JoinedUpdateIds = [];
                return ids;
            }
        }

        public (string? DocumentId, long ClientId, string? NewSeedOwner, string? PendingHtml) Leave(string connectionId)
        {
            lock (_lock)
            {
                if (!_sessionsByConnection.Remove(connectionId, out var session))
                    return (null, 0, null, null);

                if (!_rooms.TryGetValue(session.DocumentId, out Room? room))
                    return (session.DocumentId, session.ClientId, null, null);

                room.Members.Remove(connectionId);
                string? newOwner = null;
                string? pendingHtml = null;

                if (room.SeedOwner == connectionId)
                {
                    newOwner = room.Members.FirstOrDefault(m => m.Value.CanEdit).Key;
                    room.SeedOwner = newOwner;
                    pendingHtml = room.InMemoryInitialHtml;
                }

                if (room.Members.Count == 0)
                    _rooms.Remove(session.DocumentId);

                return (session.DocumentId, session.ClientId, newOwner, pendingHtml);
            }
        }

        /// <summary>
        /// Permite desconectar todos los WebSockets activos si se deshabilita la colaboración en caliente.[cite: 5]
        /// </summary>
        public void KillAllConnections()
        {
            lock (_lock)
            {
                foreach (var session in _sessionsByConnection.Values)
                {
                    try { session.HubContext?.Abort(); } catch { }
                }
                _sessionsByConnection.Clear();
                _rooms.Clear();
            }
        }

        private Room GetRoom(string documentId)
        {
            if (!_rooms.TryGetValue(documentId, out Room? room))
                _rooms[documentId] = room = new Room();
            return room;
        }
    }
}
