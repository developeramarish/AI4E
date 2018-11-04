using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AI4E.Internal;
using AI4E.Processing;
using AI4E.Remoting;
using Microsoft.Extensions.Logging;
using static System.Diagnostics.Debug;

namespace AI4E.Routing.SignalR.Server
{
    // TODO: Rename
    public sealed class ClientManager : IDisposable
    {
        private readonly IRequestReplyServerEndPoint _endPoint;
        private readonly IMessageRouterFactory _messageRouterFactory;
        private readonly IEndPointManager _endPointManager;
        private readonly IConnectedClientLookup _clientLookup;
        private readonly ILogger<ClientManager> _logger;

        private readonly Dictionary<EndPointAddress, (IMessageRouter router, Task disonnectionTask)> _routers;
        private readonly object _routersLock = new object();

        private readonly IAsyncProcess _receiveProcess;
        private bool _isDisposed;

        public ClientManager(IRequestReplyServerEndPoint endPoint,
                             IMessageRouterFactory messageRouterFactory,
                             IEndPointManager endPointManager,
                             IConnectedClientLookup clientLookup,
                             ILogger<ClientManager> logger)
        {
            if (endPoint == null)
                throw new ArgumentNullException(nameof(endPoint));

            if (messageRouterFactory == null)
                throw new ArgumentNullException(nameof(messageRouterFactory));

            if (endPointManager == null)
                throw new ArgumentNullException(nameof(endPointManager));

            if (clientLookup == null)
                throw new ArgumentNullException(nameof(clientLookup));


            _endPoint = endPoint;
            _messageRouterFactory = messageRouterFactory;
            _endPointManager = endPointManager;
            _clientLookup = clientLookup;
            _logger = logger;

            _routers = new Dictionary<EndPointAddress, (IMessageRouter router, Task disonnectionTask)>();
            _receiveProcess = new AsyncProcess(ReceiveProcess, start: true);
        }

        #region Encoding/Decoding

        private static async Task EncodeRouteResponseAsync(IMessage message, IReadOnlyCollection<IMessage> routeResponse, CancellationToken cancellation)
        {
            using (var stream = message.PushFrame().OpenStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(routeResponse.Count);

                foreach (var response in routeResponse)
                {
                    response.Trim();

                    writer.Write(response.Length);
                    await response.WriteAsync(stream, cancellation);
                }
            }
        }

        private static void EncodeHandleRequest(IMessage message, string route, bool publish)
        {
            var routeBytes = Encoding.UTF8.GetBytes(route);

            using (var stream = message.PushFrame().OpenStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((short)MessageType.Handle);
                writer.Write((short)0); // Padding
                writer.Write(routeBytes.Length);
                writer.Write(routeBytes);
                writer.Write(publish);
            }
        }

        #endregion

        #region Routers

        private IMessageRouter GetRouter(EndPointAddress endPoint)
        {
            lock (_routersLock)
            {
                if (_routers.TryGetValue(endPoint, out var entry))
                {
                    return entry.router;
                }

                var disonnectionTask = _clientLookup.WaitForDisconnectAsync(endPoint, cancellation: default);

                if (disonnectionTask.IsCompleted)
                {
                    return null;
                }

                var router = CreateRouter(endPoint);

                async Task ClientDisconnectionWithEntryRemoval()
                {
                    try
                    {
                        await disonnectionTask;
                    }
                    finally
                    {
                        lock (_routersLock)
                        {
                            _routers.Remove(endPoint);
                        }

                        router.Dispose();
                    }
                }

                _routers.Add(endPoint, (router, ClientDisconnectionWithEntryRemoval()));

                return router;
            }
        }

        private IMessageRouter CreateRouter(EndPointAddress endPoint)
        {
            // TODO: Get the clients route options and combine them with RouteOptions.PublishOnly. 
            //       This is not necessary for now as there are no other options currently.
            var messageRouter = _messageRouterFactory.CreateMessageRouter(endPoint,
                                                                          new SerializedMessageHandlerProxy(this, endPoint),
                                                                          RouteOptions.PublishOnly); // We allow publishing only.
            return messageRouter;
        }

        private sealed class SerializedMessageHandlerProxy : ISerializedMessageHandler
        {
            private readonly ClientManager _owner;
            private readonly EndPointAddress _endPoint;

            public SerializedMessageHandlerProxy(ClientManager owner, EndPointAddress endPoint)
            {
                Assert(owner != null);
                Assert(endPoint != default);
                _owner = owner;
                _endPoint = endPoint;
            }

            public async ValueTask<IMessage> HandleAsync(string route, IMessage serializedMessage, bool publish, CancellationToken cancellation = default)
            {
                var frameIdx = serializedMessage.FrameIndex;

                try
                {
                    var message = new Message();

                    do
                    {
                        using (var readStream = serializedMessage.PopFrame().OpenStream())
                        using (var writeStream = message.PushFrame().OpenStream())
                        {
                            readStream.CopyTo(writeStream);
                        }
                    }
                    while (serializedMessage.FrameIndex > -1);

                    EncodeHandleRequest(message, route, publish);

                    var response = await _owner._endPoint.SendAsync(message, _endPoint, cancellation);
                    return response;
                }
                finally
                {
                    Assert(serializedMessage.FrameIndex <= frameIdx);

                    while (serializedMessage.FrameIndex < frameIdx)
                    {
                        serializedMessage.PushFrame();
                    }
                }
            }
        }

        #endregion

        #region Receive

        private async Task ReceiveProcess(CancellationToken cancellation)
        {
            // We cache the delegate for perf reasons.
            var handler = new Func<IMessage, EndPointAddress, CancellationToken, Task<IMessage>>(HandleAsync);

            while (cancellation.ThrowOrContinue())
            {
                try
                {
                    var receiveResult = await _endPoint.ReceiveAsync(cancellation);
                    receiveResult.HandleAsync(handler, cancellation).HandleExceptions(_logger);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
                catch (Exception exc)
                {
                    // TODO: Log
                }
            }
        }

        private async Task<IMessage> HandleAsync(IMessage message, EndPointAddress remoteEndPoint, CancellationToken cancellation)
        {
            using (var stream = message.PopFrame().OpenStream())
            using (var reader = new BinaryReader(stream))
            {
                var messageType = (MessageType)reader.ReadInt16();
                reader.ReadInt16();

                var router = GetRouter(remoteEndPoint);

                switch (messageType)
                {
                    // TODO: If a message cannot be routed, there seems to be no answer to the client sent.
                    case MessageType.Route:
                        {
                            var routesCount = reader.ReadInt32();
                            var routes = new string[routesCount];
                            for (var i = 0; i < routesCount; i++)
                            {
                                var routeBytesLength = reader.ReadInt32();
                                var routeBytes = reader.ReadBytes(routeBytesLength);
                                routes[i] = Encoding.UTF8.GetString(routeBytes);
                            }

                            var publish = reader.ReadBoolean();
                            var routeResponse = await router.RouteAsync(routes, message, publish, cancellation);
                            var response = new Message();
                            await EncodeRouteResponseAsync(response, routeResponse, cancellation);
                            return response;
                        }

                    case MessageType.RouteToEndPoint:
                        {
                            var routeBytesLength = reader.ReadInt32();
                            var routeBytes = reader.ReadBytes(routeBytesLength);
                            var route = Encoding.UTF8.GetString(routeBytes);
                            var endPoint = reader.ReadEndPointAddress();
                            var publish = reader.ReadBoolean();
                            var response = await router.RouteAsync(route, message, publish, endPoint, cancellation);
                            return response;
                        }

                    case MessageType.RegisterRoute:
                    case MessageType.UnregisterRoute:
                        {
                            var routeBytesLength = reader.ReadInt32();
                            var routeBytes = reader.ReadBytes(routeBytesLength);
                            var route = Encoding.UTF8.GetString(routeBytes);

                            if (messageType == MessageType.RegisterRoute)
                            {
                                await router.RegisterRouteAsync(route, cancellation);
                            }
                            else
                            {
                                await router.UnregisterRouteAsync(route, cancellation);
                            }

                            return null;
                        }

                    default:
                        {
                            // TODO: Send bad request message
                            // TODO: Log

                            return null;
                        }
                }
            }
        }

        #endregion

        #region Disposal

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;

                _receiveProcess.Terminate();
            }
        }
        #endregion

        private enum MessageType : short
        {
            Route = 0,
            RouteToEndPoint = 1,
            RegisterRoute = 2,
            UnregisterRoute = 3,
            Handle = 5
        }
    }
}
