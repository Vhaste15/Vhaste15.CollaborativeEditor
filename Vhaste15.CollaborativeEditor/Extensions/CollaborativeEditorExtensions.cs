using Vhaste15.CollaborativeEditor.Abstractions;
using Vhaste15.CollaborativeEditor.Hubs;
using Vhaste15.CollaborativeEditor.Internal;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System;
using System.Collections.Generic;
using System.Text;

namespace Vhaste15.CollaborativeEditor.Extensions
{
    public static class CollaborativeEditorExtensions
    {
        /// <summary>
        /// Registra el motor de edición colaborativa en tiempo real (funciona en memoria por defecto).
        /// </summary>
        public static IServiceCollection AddCollaborativeEditor(
            this IServiceCollection services,
            Action<CollaborationOptions>? configure = null)
        {
            var options = new CollaborationOptions();
            configure?.Invoke(options);

            services.Configure<CollaborationOptions>(opt =>
            {
                opt.Enabled = options.Enabled;
                opt.DocumentIdRegex = options.DocumentIdRegex;
                opt.CompactionThreshold = options.CompactionThreshold;
                opt.MaxBytesUpdate = options.MaxBytesUpdate;
                opt.MaxBytesState = options.MaxBytesState;
                opt.MaxBytesAwareness = options.MaxBytesAwareness;
                opt.KeepAliveIntervalSeconds = options.KeepAliveIntervalSeconds;
            });

            services.AddSingleton<CollaborationRooms>();

            // Usa el resolutor de permisos JWT por defecto solo si la app no registró uno propio
            services.TryAddScoped<ICollaborativePermissionResolver, JwtClaimPermissionResolver>();

            services.AddSignalR(signalROptions =>
            {
                signalROptions.KeepAliveInterval = TimeSpan.FromSeconds(options.KeepAliveIntervalSeconds); //[cite: 5]
            })
            .AddHubOptions<EditorHub>(hubOptions =>
            {
                hubOptions.MaximumReceiveMessageSize = options.MaxBytesState;
            });

            // Asegura que MVC descubra el CollaborationController embebido en este ensamblado NuGet
            services.AddControllers()
                .AddApplicationPart(typeof(CollaborativeEditorExtensions).Assembly);

            return services;
        }

        /// <summary>
        /// Permite a la aplicación consumidora registrar su propia persistencia en base de datos (Opcional).
        /// </summary>
        public static IServiceCollection AddCollaborativePersistence<TPersistence>(this IServiceCollection services)
            where TPersistence : class, ICollaborativePersistence
        {
            services.AddScoped<ICollaborativePersistence, TPersistence>();
            return services;
        }

        /// <summary>
        /// Mapea el Hub anónimo de edición colaborativa.
        /// </summary>
        public static HubEndpointConventionBuilder MapCollaborativeEditorHub(
            this IEndpointRouteBuilder endpoints,
            string routePattern = "/hubs/editor")
        {
            return endpoints.MapHub<EditorHub>(routePattern);
        }
    }
}
