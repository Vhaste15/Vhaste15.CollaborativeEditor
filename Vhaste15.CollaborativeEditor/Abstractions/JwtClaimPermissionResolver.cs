using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text;

namespace Vhaste15.CollaborativeEditor.Abstractions
{
    internal sealed class JwtClaimPermissionResolver : ICollaborativePermissionResolver
    {
        public Task<DocumentPermission> ResolveAsync(ClaimsPrincipal user, string documentId, CancellationToken ct)
        {
            if (user.IsInRole("Admin") || user.HasClaim("permission", "editor:edit") || user.HasClaim("doc_edit", documentId))
                return Task.FromResult(DocumentPermission.Edit);

            if (user.IsInRole("Viewer") || user.HasClaim("permission", "editor:read") || user.HasClaim("doc_read", documentId))
                return Task.FromResult(DocumentPermission.Read);

            return Task.FromResult(DocumentPermission.None);
        }
    }
}
