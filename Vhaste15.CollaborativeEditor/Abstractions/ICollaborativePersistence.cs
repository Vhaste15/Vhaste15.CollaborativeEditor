using System;
using System.Collections.Generic;
using System.Text;

namespace Vhaste15.CollaborativeEditor.Abstractions
{
    public sealed record StoredUpdate(long Id, byte[] Data);

    /// <summary>
    /// Contrato opcional para que la aplicación consumidora persista los deltas de Yjs y el HTML en su BD.
    /// Si no se registra en el contenedor DI, el editor colaborativo funcionará 100% en memoria.
    /// </summary>
    public interface ICollaborativePersistence
    {
        Task<IReadOnlyList<StoredUpdate>> GetUpdatesAsync(string documentId, CancellationToken ct);
        Task AddUpdateAsync(string documentId, byte[] data, string userId, CancellationToken ct);
        Task CompactAsync(string documentId, IReadOnlyCollection<long> ids, byte[] state, string userId, CancellationToken ct);
        Task<string?> GetHtmlAsync(string documentId, CancellationToken ct);
        Task SaveHtmlAsync(string documentId, string html, string userId, CancellationToken ct);
    }
}
