﻿using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using OpenMod.API.Ioc;
using OpenMod.Core.Permissions;

namespace OpenMod.Core
{
    [UsedImplicitly]
    public class ServiceConfigurator : IServiceConfigurator
    {
        public Task ConfigureServicesAsync(IOpenModStartupContext openModStartupContext, IServiceCollection serviceCollection)
        {
            serviceCollection.Configure<PermissionCheckerOptions>(options =>
            {
                options.AddPermissionCheckProvider<DefaultPermissionCheckProvider>();
                options.AddPermissionSource<DefaultPermissionStore>();
            });

            return Task.CompletedTask;
        }
    }
}