﻿using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AI4E.Internal;
using AI4E.Processing;
using AI4E.Remoting;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using static System.Diagnostics.Debug;

namespace AI4E.Routing.SignalR.Server
{
    // TODO: (1) Logging
    //       (2) Are dead clients removed from the client lookup table?
    //       (3) If a message is sent from a caller, the message may by put to the underlying end-point, just in the time that the signalr connection is already re-established. 
    //           The logical end-point now tries to send a message to a client address that is not available any more, or more dangerous is already allocated for a completely other client.
    //       (4) There are lots of duplicates, both in the type itself and compared to LogicalClientEndPoint. Maybe a common base class can help here.
    public sealed class LogicalServerEndPoint : ILogicalServerEndPoint, IDisposable
    {
        #region Fields

        private readonly IServerEndPoint _endPoint;
        private readonly IConnectedClientLookup _connectedClients;
        private readonly ILogger<LogicalServerEndPoint> _logger;

        private readonly AsyncProducerConsumerQueue<(IMessage message, int seqNum, EndPointAddress endPoint)> _rxQueue = new AsyncProducerConsumerQueue<(IMessage message, int seqNum, EndPointAddress endPoint)>();
        private readonly ConcurrentDictionary<int, TaskCompletionSource<IMessage>> _responseTable = new ConcurrentDictionary<int, TaskCompletionSource<IMessage>>();
        private readonly ConcurrentDictionary<int, CancellationTokenSource> _cancellationTable = new ConcurrentDictionary<int, CancellationTokenSource>();
        private readonly ConcurrentDictionary<EndPointAddress, string> _clientLookup = new ConcurrentDictionary<EndPointAddress, string>();

        private readonly IAsyncProcess _receiveProcess;

        private int _nextSeqNum;

        #endregion

        #region C'tor

        public LogicalServerEndPoint(IServerEndPoint endPoint, IConnectedClientLookup connectedClients, ILogger<LogicalServerEndPoint> logger = null)
        {
            if (endPoint == null)
                throw new ArgumentNullException(nameof(endPoint));

            if (connectedClients == null)
                throw new ArgumentNullException(nameof(connectedClients));

            _endPoint = endPoint;
            _connectedClients = connectedClients;
            _logger = logger;

            _receiveProcess = new AsyncProcess(ReceiveProcess, start: true);
        }

        #endregion

        #region ILogicalServerEndPoint

        public async Task<IMessage> SendAsync(IMessage message, EndPointAddress endPoint, CancellationToken cancellation)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            if (endPoint == default)
                throw new ArgumentDefaultException(nameof(endPoint));

            using (CheckDisposal(ref cancellation, out var externalCancellation, out var disposal))
            {
                try
                {
                    var seqNum = GetNextSeqNum();
                    var responseSource = new TaskCompletionSource<IMessage>();

                    while (!_responseTable.TryAdd(seqNum, responseSource))
                    {
                        seqNum = GetNextSeqNum();
                    }

                    void RequestCancellation()
                    {
                        var cancellationRequest = new Message();
                        EncodeServerMessage(cancellationRequest, GetNextSeqNum(), corr: seqNum, MessageType.CancellationRequest);
                        SendInternalAsync(cancellationRequest, endPoint, cancellation: default).HandleExceptions(_logger);
                    }

                    EncodeServerMessage(message, seqNum, corr: default, MessageType.Request);

                    using (cancellation.Register(RequestCancellation))
                    {
                        await Task.WhenAll(SendInternalAsync(message, endPoint, cancellation), responseSource.Task);
                    }

                    return await responseSource.Task;

                }
                catch (OperationCanceledException) when (disposal.IsCancellationRequested)
                {
                    throw new ObjectDisposedException(GetType().FullName);
                }
            }
        }

        public async Task<IMessageReceiveResult<EndPointAddress>> ReceiveAsync(CancellationToken cancellation)
        {
            using (CheckDisposal(ref cancellation, out var externalCancellation, out var disposal))
            {
                try
                {
                    var (message, seqNum, endPoint) = await _rxQueue.DequeueAsync(cancellation);

                    return new MessageReceiveResult(this, message, seqNum, endPoint);
                }
                catch (OperationCanceledException) when (disposal.IsCancellationRequested)
                {
                    throw new ObjectDisposedException(GetType().FullName);
                }
            }
        }

        private sealed class MessageReceiveResult : IMessageReceiveResult<EndPointAddress>
        {
            private readonly LogicalServerEndPoint _logicalEndPoint;
            private readonly int _seqNum;
            private readonly CancellationTokenSource _cancellationRequestSource;

            public MessageReceiveResult(LogicalServerEndPoint logicalEndPoint, IMessage message, int seqNum, EndPointAddress remoteEndPoint)
            {
                _logicalEndPoint = logicalEndPoint;
                Message = message;
                _seqNum = seqNum;
                RemoteEndPoint = remoteEndPoint;

                _cancellationRequestSource = _logicalEndPoint._cancellationTable.GetOrAdd(seqNum, new CancellationTokenSource());
            }

            public CancellationToken Cancellation => _cancellationRequestSource.Token;

            public IMessage Message { get; }

            public EndPointAddress RemoteEndPoint { get; }

            public Task SendResponseAsync(IMessage response)
            {
                if (response == null)
                    throw new ArgumentNullException(nameof(response));

                return InternalSendResponseAsync(response);
            }

            public Task SendAckAsync()
            {
                return InternalSendResponseAsync(response: null);
            }

            public Task SendCancellationAsync()
            {
                var cancellationResponse = new Message();
                EncodeServerMessage(cancellationResponse, seqNum: _logicalEndPoint.GetNextSeqNum(), corr: _seqNum, MessageType.CancellationResponse);
                return _logicalEndPoint.SendInternalAsync(cancellationResponse, RemoteEndPoint, cancellation: default);
            }

            private Task InternalSendResponseAsync(IMessage response)
            {
                if (response == null)
                {
                    response = new Message();
                }

                EncodeServerMessage(response, seqNum: _logicalEndPoint.GetNextSeqNum(), corr: _seqNum, MessageType.Response);

                return _logicalEndPoint.SendInternalAsync(response, RemoteEndPoint, Cancellation);
            }

            public void Dispose()
            {
                _cancellationRequestSource.Dispose();
                _logicalEndPoint._cancellationTable.Remove(_seqNum, _cancellationRequestSource);
            }
        }

        #endregion

        #region Encode/Decode

        // These are the counterparts of the encode/decode functions in LogicalClientEndPoint. These must be in sync.
        // TODO: Create a base class and move the functions there.

        private static void EncodeServerMessage(IMessage message, int seqNum, int corr, MessageType messageType)
        {
            using (var stream = message.PushFrame().OpenStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(seqNum); // 4 bytes
                writer.Write((byte)messageType); // 1 bytes
                writer.Write((byte)0); // 1 bytes (padding)
                writer.Write((byte)0); // 1 bytes (padding)
                writer.Write((byte)0); // 1 bytes (padding)
                writer.Write(corr); // 4 bytes
            }
        }

        private static (int seqNum, int corr, MessageType messageType, EndPointAddress remoteEndPoint, string securityToken) DecodeClientMessage(IMessage message)
        {
            EndPointAddress remoteEndPoint;
            string securityToken;

            using (var stream = message.PopFrame().OpenStream())
            using (var reader = new BinaryReader(stream))
            {
                var seqNum = reader.ReadInt32(); // 4 bytes
                var messageType = (MessageType)reader.ReadByte(); // 1 bytes
                reader.ReadByte(); // 1 bytes (padding)
                reader.ReadByte(); // 1 bytes (padding)
                reader.ReadByte(); // 1 bytes (padding)
                var corr = reader.ReadInt32(); // 4 bytes

                remoteEndPoint = reader.ReadEndPointAddress();

                var securityTokenBytesLength = reader.ReadInt32(); // 4 bytes

                if (securityTokenBytesLength > 0)
                {
                    var securityTokenBytes = reader.ReadBytes(securityTokenBytesLength); // Variable length
                    securityToken = Encoding.UTF8.GetString(securityTokenBytes);
                }
                else
                {
                    securityToken = null;
                }

                return (seqNum, corr, messageType, remoteEndPoint, securityToken);
            }
        }

        // This must be in sync with LogicalClientEndPoint.DecodeInitResponse
        private static void EncodeInitResponse(IMessage result, EndPointAddress endPoint, string securityToken)
        {
            var securityTokenBytes = Encoding.UTF8.GetBytes(securityToken);

            using (var stream = result.PushFrame().OpenStream())
            {
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(endPoint);

                    writer.Write(securityTokenBytes.Length);
                    writer.Write(securityTokenBytes);
                }
            }
        }

        #endregion

        #region Send

        // The message is encoded (Our message frame is on tos)
        private Task SendInternalAsync(IMessage message, string address, CancellationToken cancellation)
        {
            return _endPoint.SendAsync(message, address, cancellation);
        }

        // The message is encoded (Our message frame is on tos)
        private Task SendInternalAsync(IMessage message, EndPointAddress endPoint, CancellationToken cancellation)
        {
            var address = LookupAddress(endPoint);

            if (address == null)
            {
                throw new Exception($"The client '{endPoint}' is unreachable."); // TODO
            }

            return SendInternalAsync(message, address, cancellation);
        }

        private string LookupAddress(EndPointAddress endPoint)
        {
            if (_clientLookup.TryGetValue(endPoint, out var address))
            {
                return address;
            }

            return null;
        }

        #endregion

        #region Receive

        private async Task ReceiveProcess(CancellationToken cancellation)
        {
            while (cancellation.ThrowOrContinue())
            {
                try
                {
                    var (message, address) = await _endPoint.ReceiveAsync(cancellation);
                    Task.Run(() => ReceiveInternalAsync(message, address, cancellation)).HandleExceptions(_logger);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
                catch (Exception exc)
                {
                    // TODO: Log
                }
            }
        }

        // The message is encoded (Our message frame is on tos)
        private async Task ReceiveInternalAsync(IMessage message, string address, CancellationToken cancellation)
        {
            var (seqNum, corr, messageType, remoteEndPoint, securityToken) = DecodeClientMessage(message);

            if (messageType == MessageType.Init)
            {
                await ReceiveInitAsync(message, seqNum, corr, address, cancellation);
                return;
            }

            if (!await ValidateIntegrityAsync(address, remoteEndPoint, securityToken, cancellation))
            {
                // TODO: Send bad client response
                // TODO: Log bad request

                return;
            }

            switch (messageType)
            {
                case MessageType.Request:
                    await ReceiveRequestAsync(message, seqNum, remoteEndPoint, cancellation);
                    break;

                case MessageType.Response:
                    await ReceiveResponseAsync(message, seqNum, corr, cancellation);
                    break;

                case MessageType.CancellationRequest:
                    await ReceiveCancellationRequestAsync(message, seqNum, corr, cancellation);
                    break;

                case MessageType.CancellationResponse:
                    await ReceiveCancellationResponseAsnyc(message, seqNum, corr, cancellation);
                    break;

                default:
                    // Unknown message type. TODO: Log
                    break;
            }
        }

        private async Task<bool> ValidateIntegrityAsync(string address, EndPointAddress remoteEndPoint, string securityToken, CancellationToken cancellation)
        {
            var result = await _connectedClients.ValidateClientAsync(remoteEndPoint, securityToken, cancellation);

            if (result)
            {
                _clientLookup.AddOrUpdate(remoteEndPoint, address, (_, entry) => address);
            }

            return result;
        }

        private async Task ReceiveInitAsync(IMessage message, int seqNum, int corr, string address, CancellationToken cancellation)
        {
            var cancellationRequestSource = _cancellationTable.GetOrAdd(seqNum, _ => new CancellationTokenSource());
            var cancellationRequest = cancellationRequestSource.Token;

            var combinedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationRequest, cancellation).Token;

            EndPointAddress endPoint;
            string securityToken;

            try
            {

                (endPoint, securityToken) = await _connectedClients.AddClientAsync(combinedCancellation);

                var sucess = _clientLookup.TryAdd(endPoint, address);
                Assert(sucess);
            }
            catch (OperationCanceledException) when (cancellationRequest.IsCancellationRequested)
            {
                var cancellationResponse = new Message();

                EncodeServerMessage(cancellationResponse, seqNum: GetNextSeqNum(), corr: seqNum, MessageType.CancellationResponse);

                await SendInternalAsync(cancellationResponse, address, combinedCancellation);

                return;
            }

            var initResponse = new Message();

            EncodeInitResponse(initResponse, endPoint, securityToken);
            EncodeServerMessage(initResponse, GetNextSeqNum(), corr: seqNum, MessageType.Response);

            await SendInternalAsync(initResponse, address, combinedCancellation);
        }

        private Task ReceiveRequestAsync(IMessage message, int seqNum, EndPointAddress remoteEndPoint, CancellationToken cancellation)
        {
            _cancellationTable.GetOrAdd(seqNum, _ => new CancellationTokenSource());

            return _rxQueue.EnqueueAsync((message, seqNum, remoteEndPoint), cancellation);
        }

        private Task ReceiveResponseAsync(IMessage message, int seqNum, int corr, CancellationToken cancellation)
        {
            // We did not already receive a response for this corr-id.
            if (_responseTable.TryGetValue(corr, out var responseSource))
            {
                responseSource.SetResult(message);
            }

            return Task.CompletedTask;
        }

        private Task ReceiveCancellationRequestAsync(IMessage message, int seqNum, int corr, CancellationToken cancellation)
        {
            if (_cancellationTable.TryGetValue(corr, out var cancellationSource))
            {
                cancellationSource.Cancel();
            }

            return Task.CompletedTask;
        }

        private Task ReceiveCancellationResponseAsnyc(IMessage message, int seqNum, int corr, CancellationToken cancellation)
        {
            // We did not already receive a response for this corr-id.
            if (_responseTable.TryGetValue(corr, out var responseSource))
            {
                responseSource.TrySetCanceled();
            }

            return Task.CompletedTask;
        }

        #endregion

        #region Disposal

        private volatile CancellationTokenSource _disposalSource;

        public void Dispose()
        {
            var disposalSource = Interlocked.Exchange(ref _disposalSource, null);

            if (disposalSource != null)
            {
                // TODO: Log

                _receiveProcess.Terminate();
            }
        }

        private IDisposable CheckDisposal(ref CancellationToken cancellation,
                                              out CancellationToken externalCancellation,
                                              out CancellationToken disposal)
        {
            var disposalSource = _disposalSource; // Volatile read op

            if (disposalSource == null)
                throw new ObjectDisposedException(GetType().FullName);

            externalCancellation = cancellation;
            disposal = disposalSource.Token;

            if (cancellation.CanBeCanceled)
            {
                var combinedCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellation, disposal);
                cancellation = combinedCancellationSource.Token;

                return combinedCancellationSource;
            }
            else
            {
                cancellation = disposal;

                return NoOpDisposable.Instance;
            }
        }

        #endregion

        private int GetNextSeqNum()
        {
            return Interlocked.Increment(ref _nextSeqNum);
        }

        // TODO: This is a duplicate from LogicalClientEndPoint
        private enum MessageType : byte
        {
            Init = 0,
            Request = 1,
            Response = 2,
            CancellationRequest = 3,
            CancellationResponse = 4
        }
    }
}
