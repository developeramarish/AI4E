using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using AI4E.ApplicationParts;
using AI4E.ApplicationParts.Utils;
using AI4E.Handler;
using AI4E.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static System.Diagnostics.Debug;

namespace AI4E
{
    public static class ServiceCollectionExtension
    {
        public static void ConfigureApplicationServices(this IServiceCollection services, Action<ApplicationServiceManager> configuration)
        {
            if (services == null)
                throw new ArgumentNullException(nameof(services));

            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));

            var serviceManager = services.GetService<ApplicationServiceManager>();

            if (serviceManager == null)
            {
                serviceManager = new ApplicationServiceManager();
            }

            configuration(serviceManager);
            services.TryAddSingleton(serviceManager);
        }

        public static IServiceCollection AddCoreServices(this IServiceCollection services)
        {
            if (services == null)
                throw new ArgumentNullException(nameof(services));

            // This must be registered as transient to allow retrieval of scoped services.
            services.TryAddTransient(typeof(IProvider<>), typeof(Provider<>));
            services.TryAddSingleton(typeof(IContextualProvider<>), typeof(ContextualProvider<>));
            services.TryAddSingleton<IDateTimeProvider, DateTimeProvider>();

            return services;
        }

        private sealed class Provider<T> : IProvider<T>
        {
            private readonly IServiceProvider _serviceProvider;

            public Provider(IServiceProvider serviceProvider)
            {
                Assert(serviceProvider != null);
                _serviceProvider = serviceProvider;
            }

            public T ProvideInstance()
            {
                return _serviceProvider.GetRequiredService<T>();
            }
        }

        private sealed class ContextualProvider<T> : IContextualProvider<T>
        {
            public ContextualProvider() { }

            public T ProvideInstance(IServiceProvider serviceProvider)
            {
                if (serviceProvider == null)
                    throw new ArgumentNullException(nameof(serviceProvider));

                return serviceProvider.GetRequiredService<T>();
            }
        }

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

            services.TryAddSingleton<IMessageHandlerRegistry, MessageHandlerRegistry>();
            services.Decorate<IMessageHandlerRegistry>(BuildMessageHandlerRegistry);

            services.AddSingleton<IMessageDispatcher, TMessageDispatcher>();
        }

        public static void AddMessageDispatcher<TMessageDispatcher>(this IServiceCollection services, TMessageDispatcher instance)
            where TMessageDispatcher : class, IMessageDispatcher
        {
            if (services == null)
                throw new ArgumentNullException(nameof(services));

            if (instance == null)
                throw new ArgumentNullException(nameof(instance));

            services.ConfigureApplicationParts(ConfigureFeatureProviders);

            services.TryAddSingleton<IMessageHandlerRegistry, MessageHandlerRegistry>();
            services.Decorate<IMessageHandlerRegistry>(BuildMessageHandlerRegistry);

            services.AddSingleton<IMessageDispatcher>(instance);
        }

        public static void AddMessageDispatcher<TMessageDispatcher>(this IServiceCollection services, Func<IServiceProvider, TMessageDispatcher> factory)
            where TMessageDispatcher : class, IMessageDispatcher
        {
            if (services == null)
                throw new ArgumentNullException(nameof(services));

            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            services.ConfigureApplicationParts(ConfigureFeatureProviders);

            services.TryAddSingleton<IMessageHandlerRegistry, MessageHandlerRegistry>();
            services.Decorate<IMessageHandlerRegistry>(BuildMessageHandlerRegistry);

            services.AddSingleton<IMessageDispatcher>(factory);
        }

        public static void AddMessageDispatcher<TMessageDispatcher, TMessageDispatcherImpl>(this IServiceCollection services)
            where TMessageDispatcher : class, IMessageDispatcher
            where TMessageDispatcherImpl : class, TMessageDispatcher
        {
            if (services == null)
                throw new ArgumentNullException(nameof(services));

            services.ConfigureApplicationParts(ConfigureFeatureProviders);

            services.TryAddSingleton<IMessageHandlerRegistry, MessageHandlerRegistry>();
            services.Decorate<IMessageHandlerRegistry>(BuildMessageHandlerRegistry);

            services.AddSingleton<TMessageDispatcher, TMessageDispatcherImpl>();
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

            services.TryAddSingleton<IMessageHandlerRegistry, MessageHandlerRegistry>();
            services.Decorate<IMessageHandlerRegistry>(BuildMessageHandlerRegistry);

            services.AddSingleton<TMessageDispatcher>(instance);
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

            services.TryAddSingleton<IMessageHandlerRegistry, MessageHandlerRegistry>();
            services.Decorate<IMessageHandlerRegistry>(BuildMessageHandlerRegistry);

            services.AddSingleton<TMessageDispatcher>(factory);
            services.AddSingleton<IMessageDispatcher>(provider => provider.GetRequiredService<TMessageDispatcher>());
        }

        public static IMessageHandlerRegistry BuildMessageHandlerRegistry(IMessageHandlerRegistry messageHandlerRegistry, IServiceProvider serviceProvider)
        {
            if (serviceProvider == null)
                throw new ArgumentNullException(nameof(serviceProvider));

            if (messageHandlerRegistry == null)
                throw new ArgumentNullException(nameof(messageHandlerRegistry));

            var options = serviceProvider.GetService<IOptions<MessagingOptions>>()?.Value ?? new MessagingOptions();
            var processors = options.MessageProcessors.ToImmutableArray();
            var partManager = serviceProvider.GetRequiredService<ApplicationPartManager>();
            var messageHandlerFeature = new MessageHandlerFeature();

            partManager.PopulateFeature(messageHandlerFeature);

            return messageHandlerRegistry;
        }

        private static void RegisterMessageHandlerTypes(IMessageHandlerRegistry messageHandlerRegistry,
                                                        ImmutableArray<IContextualProvider<IMessageProcessor>> processors,
                                                        IEnumerable<Type> types,
                                                        ILogger logger)
        {
            foreach (var type in types)
            {
                RegisterMessageHandlerType(messageHandlerRegistry, processors, type, logger);
            }
        }

        private static void RegisterMessageHandlerType(IMessageHandlerRegistry messageHandlerRegistry,
                                                       ImmutableArray<IContextualProvider<IMessageProcessor>> processors,
                                                       Type type,
                                                       ILogger logger)
        {
            var inspector = new MessageHandlerInspector(type);
            var descriptors = inspector.GetHandlerDescriptors();

            foreach (var descriptor in descriptors)
            {
                var messageType = descriptor.MessageType;
                var factory = (IMessageHandlerFactory)Activator.CreateInstance(
                    typeof(MessageHandlerProvider<>).MakeGenericType(messageType),
                    type,
                    descriptor,
                    processors);

                messageHandlerRegistry.Register(factory);

                logger?.LogDebug($"Registered handler of type '{type}' for message-type '{messageType}'.");
            }
        }

        private static void ConfigureFeatureProviders(ApplicationPartManager partManager)
        {
            if (!partManager.FeatureProviders.OfType<MessageHandlerFeatureProvider>().Any())
            {
                partManager.FeatureProviders.Add(new MessageHandlerFeatureProvider());
            }
        }
    }
}
