using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text;

namespace Vhaste15.CollaborativeEditor.Abstractions
{
    public enum DocumentPermission { None, Read, Edit }

    /// <summary>
    /// Permite personalizar cómo se resuelven los permisos.
    /// Si no se registra, la librería incluye una implementación por defecto basada en los claims del JWT.
    /// </summary>
    public interface ICollaborativePermissionResolver
    {
        Task<DocumentPermission> ResolveAsync(ClaimsPrincipal user, string documentId, CancellationToken ct);
    }
}
