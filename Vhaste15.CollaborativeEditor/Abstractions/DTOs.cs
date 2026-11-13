using System;
using System.Collections.Generic;
using System.Text;

namespace Vhaste15.CollaborativeEditor.Abstractions
{
    public interface IEditorClient
    {
        Task ReceiveUpdate(byte[] update);
        Task ReceiveAwareness(byte[] update);
        Task PeerJoined(long clientId);
        Task PeerLeft(long clientId);
        Task SeedRequested(string html);
    }

    public sealed record JoinRequest(
        string ConnectionId,
        string DocumentId,
        long ClientId,
        string? InitialHtml = null);

    public sealed record JoinResult(
        IReadOnlyList<byte[]> Updates,
        bool CanEdit,
        string? SeedHtml,
        bool Seeding,
        bool Compact);

    public sealed class CollaborationOptions
    {
        public bool Enabled { get; set; } = true;
        public string DocumentIdRegex { get; set; } = "^[a-zA-Z0-9_-]{1,100}$";
        public int CompactionThreshold { get; set; } = 200;
        public int MaxBytesUpdate { get; set; } = 1024 * 1024;      // 1 MB
        public int MaxBytesState { get; set; } = 2 * 1024 * 1024;   // 2 MB
        public int MaxBytesAwareness { get; set; } = 16 * 1024;     // 16 KB
        public int KeepAliveIntervalSeconds { get; set; } = 15;     //
    }
}
