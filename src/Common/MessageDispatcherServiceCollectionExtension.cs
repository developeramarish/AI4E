﻿/* Summary
 * --------------------------------------------------------------------------------------------------------------------
 * Filename:        ServiceCollectionExtension.cs 
 * Types:           AI4E.ServiceCollectionExtension
 * Version:         1.0
 * Author:          Andreas Trütschel
 * Last modified:   19.01.2018 
 * --------------------------------------------------------------------------------------------------------------------
 */

/* License
 * --------------------------------------------------------------------------------------------------------------------
 * This file is part of the AI4E distribution.
 *   (https://github.com/AI4E/AI4E)
 * Copyright (c) 2018 Andreas Truetschel and contributors.
 * 
 * AI4E is free software: you can redistribute it and/or modify  
 * it under the terms of the GNU Lesser General Public License as   
 * published by the Free Software Foundation, version 3.
 *
 * AI4E is distributed in the hope that it will be useful, but 
 * WITHOUT ANY WARRANTY; without even the implied warranty of 
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU 
 * Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with this program. If not, see <http://www.gnu.org/licenses/>.
 * --------------------------------------------------------------------------------------------------------------------
 */

/* Based on
 * --------------------------------------------------------------------------------------------------------------------
 * Asp.Net Core MVC
 * Copyright (c) .NET Foundation. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not use
 * these files except in compliance with the License. You may obtain a copy of the
 * License at
 *
 * http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed
 * under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR
 * CONDITIONS OF ANY KIND, either express or implied. See the License for the
 * specific language governing permissions and limitations under the License.
 * --------------------------------------------------------------------------------------------------------------------
 */

using System;
using AI4E;
using AI4E.Internal;
using Microsoft.Extensions.DependencyInjection;
using static AI4E.Internal.MessageDispatcherBuilder;

namespace AI4E
{
    public static class MessageDispatcherServiceCollectionExtension
    {
        public static IMessagingBuilder AddInMemoryMessaging(this IServiceCollection services)
        {
            if (services == null)
                throw new ArgumentNullException(nameof(services));

            services.AddMessageDispatcher<MessageDispatcher>();

            return new MessagingBuilder(services);
        }

        public static IMessagingBuilder AddInMemoryMessaging(this IServiceCollection services, Action<MessagingOptions> configuration)
        {
            if (services == null)
                throw new ArgumentNullException(nameof(services));

            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));

            var builder = services.AddInMemoryMessaging();
            builder.Services.Configure(configuration);
            return builder;
        }

        public static void AddMessageDispatcher<TMessageDispatcher>(this IServiceCollection services)
            where TMessageDispatcher : class, IMessageDispatcher
        {
            if (services == null)
                throw new ArgumentNullException(nameof(services));

            services.ConfigureApplicationParts(ConfigureFeatureProviders);

            services.AddSingleton<IMessageDispatcher>(serviceProvider => BuildMessageDispatcher(serviceProvider, ActivatorUtilities.CreateInstance<TMessageDispatcher>(serviceProvider)));
        }

        public static void AddMessageDispatcher<TMessageDispatcher>(this IServiceCollection services, TMessageDispatcher instance)
            where TMessageDispatcher : class, IMessageDispatcher
        {
            if (services == null)
                throw new ArgumentNullException(nameof(services));

            if (instance == null)
                throw new ArgumentNullException(nameof(instance));

            services.ConfigureApplicationParts(ConfigureFeatureProviders);

            services.AddSingleton<IMessageDispatcher>(serviceProvider => BuildMessageDispatcher(serviceProvider, instance));
        }

        public static void AddMessageDispatcher<TMessageDispatcher>(this IServiceCollection services, Func<IServiceProvider, TMessageDispatcher> factory)
            where TMessageDispatcher : class, IMessageDispatcher
        {
            if (services == null)
                throw new ArgumentNullException(nameof(services));

            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            services.ConfigureApplicationParts(ConfigureFeatureProviders);

            services.AddSingleton<IMessageDispatcher>(serviceProvider => BuildMessageDispatcher(serviceProvider, factory(serviceProvider)));
        }

        public static void AddMessageDispatcher<TMessageDispatcher, TMessageDispatcherImpl>(this IServiceCollection services)
            where TMessageDispatcher : class, IMessageDispatcher
            where TMessageDispatcherImpl : class, TMessageDispatcher
        {
            if (services == null)
                throw new ArgumentNullException(nameof(services));

            services.ConfigureApplicationParts(ConfigureFeatureProviders);

            services.AddSingleton<TMessageDispatcher>(serviceProvider => BuildMessageDispatcher(serviceProvider, ActivatorUtilities.CreateInstance<TMessageDispatcherImpl>(serviceProvider)));
            services.AddSingleton<IMessageDispatcher>(provider => provider.GetRequiredService<TMessageDispatcher>());
        }

        public static void AddMessageDispatcher<TMessageDispatcher, TMessageDispatcherImpl>(this IServiceCollection services, TMessageDispatcherImpl instance)
            where TMessageDispatcher : class, IMessageDispatcher
            where TMessageDispatcherImpl : class, TMessageDispatcher
        {
            if (services == null)
                throw new ArgumentNullException(nameof(services));

            if (instance == null)
                throw new ArgumentNullException(nameof(instance));

            services.ConfigureApplicationParts(ConfigureFeatureProviders);

            services.AddSingleton<TMessageDispatcher>(serviceProvider => BuildMessageDispatcher(serviceProvider, instance));
            services.AddSingleton<IMessageDispatcher>(provider => provider.GetRequiredService<TMessageDispatcher>());
        }

        public static void AddMessageDispatcher<TMessageDispatcher, TMessageDispatcherImpl>(this IServiceCollection services, Func<IServiceProvider, TMessageDispatcherImpl> factory)
            where TMessageDispatcher : class, IMessageDispatcher
            where TMessageDispatcherImpl : class, TMessageDispatcher
        {
            if (services == null)
                throw new ArgumentNullException(nameof(services));

            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            services.ConfigureApplicationParts(ConfigureFeatureProviders);

            services.AddSingleton<TMessageDispatcher>(serviceProvider => BuildMessageDispatcher(serviceProvider, factory(serviceProvider)));
            services.AddSingleton<IMessageDispatcher>(provider => provider.GetRequiredService<TMessageDispatcher>());
        }
    }
}