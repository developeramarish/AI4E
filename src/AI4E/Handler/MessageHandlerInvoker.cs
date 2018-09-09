﻿using System;
using System.Collections.Immutable;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AI4E.DispatchResults;
using AI4E.Internal;
using Microsoft.Extensions.DependencyInjection;
using static System.Diagnostics.Debug;

namespace AI4E.Handler
{
    public sealed class MessageHandlerInvoker<TMessage> : IMessageHandler<TMessage>
        where TMessage : class
    {
        private readonly object _handler;
        private readonly MessageHandlerActionDescriptor _memberDescriptor;
        private readonly ImmutableArray<IContextualProvider<IMessageProcessor>> _processors;
        private readonly IServiceProvider _serviceProvider;

        public MessageHandlerInvoker(object handler,
                                     MessageHandlerActionDescriptor memberDescriptor,
                                     ImmutableArray<IContextualProvider<IMessageProcessor>> processors,
                                     IServiceProvider serviceProvider)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            if (serviceProvider == null)
                throw new ArgumentNullException(nameof(serviceProvider));

            _handler = handler;
            _memberDescriptor = memberDescriptor;
            _processors = processors;
            _serviceProvider = serviceProvider;
        }

        public ValueTask<IDispatchResult> HandleAsync(DispatchDataDictionary<TMessage> dispatchData, CancellationToken cancellation)
        {
            Func<DispatchDataDictionary<TMessage>, ValueTask<IDispatchResult>> next = (nextDispatchData => InvokeHandlerCore(nextDispatchData, cancellation));

            for (var i = _processors.Length - 1; i >= 0; i--)
            {
                var processor = _processors[i].ProvideInstance(_serviceProvider);
                Assert(processor != null);
                var nextCopy = next; // This is needed because of the way, the variable values are captured in the lambda expression.
                next = (nextDispatchData => InvokeProcessorAsync(processor, nextDispatchData, nextCopy, cancellation));
            }

            return next(dispatchData);
        }

        private ValueTask<IDispatchResult> InvokeProcessorAsync(IMessageProcessor processor,
                                                                DispatchDataDictionary<TMessage> dispatchData,
                                                                Func<DispatchDataDictionary<TMessage>, ValueTask<IDispatchResult>> next,
                                                                CancellationToken cancellation)
        {
            var contextDescriptor = MessageProcessorContextDescriptor.GetDescriptor(processor.GetType());

            if (contextDescriptor.CanSetContext)
            {
                IMessageProcessorContext messageProcessorContext = new MessageProcessorContext(_handler, _memberDescriptor);

                contextDescriptor.SetContext(processor, messageProcessorContext);
            }

            return processor.ProcessAsync(dispatchData, next, cancellation);
        }

        private async ValueTask<IDispatchResult> InvokeHandlerCore(DispatchDataDictionary<TMessage> dispatchData, CancellationToken cancellation)
        {
            IMessageDispatchContext context = null;
            var contextDescriptor = MessageHandlerContext.GetDescriptor(_handler.GetType());

            if (contextDescriptor.CanSetContext)
            {
                context = new MessageDispatchContext(_serviceProvider, dispatchData);
                contextDescriptor.SetContext(_handler, context);
            }

            if (contextDescriptor.CanSetDispatcher)
            {
                var dispatcher = _serviceProvider.GetRequiredService<IMessageDispatcher>();
                contextDescriptor.SetDispatcher(_handler, dispatcher);
            }

            var member = _memberDescriptor.Member;
            Assert(member != null);
            var invoker = HandlerActionInvoker.GetInvoker(member);

            IMessageDispatchContext BuildContext()
            {
                return new MessageDispatchContext(_serviceProvider, dispatchData);
            }

            object ResolveParameter(ParameterInfo parameter)
            {
                if (parameter.ParameterType == typeof(IServiceProvider))
                {
                    return _serviceProvider;
                }
                else if (parameter.ParameterType == typeof(CancellationToken))
                {
                    return cancellation;
                }
                else if (parameter.ParameterType == typeof(IMessageDispatchContext))
                {
                    if (context == null)
                    {
                        context = BuildContext();
                    }

                    return context;
                }
                else if (parameter.ParameterType == typeof(DispatchDataDictionary) || parameter.ParameterType == typeof(DispatchDataDictionary<TMessage>))
                {
                    return dispatchData;
                }
                else if (parameter.IsDefined<InjectAttribute>())
                {
                    return _serviceProvider.GetRequiredService(parameter.ParameterType);
                }
                else
                {
                    return _serviceProvider.GetService(parameter.ParameterType);
                }
            }

            object result;

            try
            {
                result = await invoker.InvokeAsync(_handler, dispatchData.Message, ResolveParameter);
            }
            catch (Exception exc)
            {
                return new FailureDispatchResult(exc);
            }

            if (result == null)
            {
                if (invoker.ReturnTypeDescriptor.ResultType == typeof(void))
                {
                    return new SuccessDispatchResult();
                }

                // https://github.com/AI4E/AI4E/issues/19
                return new NotFoundDispatchResult();
            }

            return SuccessDispatchResultBuilder.GetSuccessDispatchResult(invoker.ReturnTypeDescriptor.ResultType, result);
        }

        private sealed class MessageDispatchContext : IMessageDispatchContext
        {
            public MessageDispatchContext(IServiceProvider dispatchServices, DispatchDataDictionary dispatchData)
            {
                if (dispatchServices == null)
                    throw new ArgumentNullException(nameof(dispatchServices));

                if (dispatchData == null)
                    throw new ArgumentNullException(nameof(dispatchData));

                DispatchServices = dispatchServices;
                DispatchData = dispatchData;
            }

            public IServiceProvider DispatchServices { get; }

            public DispatchDataDictionary DispatchData { get; }
        }

        private sealed class MessageProcessorContext : IMessageProcessorContext
        {
            public MessageProcessorContext(object messageHandler, MessageHandlerActionDescriptor messageHandlerAction)
            {
                if (messageHandler == null)
                    throw new ArgumentNullException(nameof(messageHandler));

                MessageHandler = messageHandler;
                MessageHandlerAction = messageHandlerAction;
            }

            public object MessageHandler { get; }
            public MessageHandlerActionDescriptor MessageHandlerAction { get; }
        }
    }
}
