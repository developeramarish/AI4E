using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AI4E.Remoting;
using AI4E.Utils;
using AI4E.Utils.Processing;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using static System.Diagnostics.Debug;

namespace AI4E.Routing
{
    public sealed class RequestReplyEndPoint<TPacket> : IRequestReplyEndPoint<TPacket> where TPacket : IPacket<TPacket>
    {
        #region Fields

        private readonly IEndPoint<TPacket> _endPoint;
        private readonly ILogger<RequestReplyEndPoint<TPacket>> _logger;

        private volatile CancellationTokenSource _disposalSource;

        private readonly AsyncProducerConsumerQueue<(int seqNum, TPacket packet)> _rxQueue;
        private readonly ConcurrentDictionary<int, TaskCompletionSource<(IMessage response, bool handled)>> _responseTable;
        private readonly ConcurrentDictionary<int, CancellationTokenSource> _cancellationTable;
        private readonly AsyncProcess _receiveProcess;

        private int _nextSeqNum = 1;

        #endregion

        #region C'tor

        public RequestReplyEndPoint(IEndPoint<TPacket> endPoint, ILogger<RequestReplyEndPoint<TPacket>> logger = null)
        {
            if (endPoint == null)
                throw new ArgumentNullException(nameof(endPoint));

            _endPoint = endPoint;
            _logger = logger;

            _disposalSource = new CancellationTokenSource();
            _rxQueue = new AsyncProducerConsumerQueue<(int seqNum, TPacket packet)>();
            _responseTable = new ConcurrentDictionary<int, TaskCompletionSource<(IMessage response, bool handled)>>();
            _cancellationTable = new ConcurrentDictionary<int, CancellationTokenSource>();

            _receiveProcess = new AsyncProcess(ReceiveProcess, start: true);
        }

        #endregion

        #region Receive

        private async Task ReceiveProcess(CancellationToken cancellation)
        {
            while (cancellation.ThrowOrContinue())
            {
                try
                {
                    var packet = await _endPoint.ReceiveAsync(cancellation);

                    await HandleMessageAsync(packet);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception exc)
                {
                    // TODO: Log
                }
            }
        }

        private async Task HandleMessageAsync(TPacket packet)
        {
            var message = packet.Message;
            var (seqNum, messageType, handled, corrId) = DecodeMessage(message);

            TaskCompletionSource<(IMessage response, bool handled)> responseSource;
            switch (messageType)
            {
                case MessageType.Request:
                    _cancellationTable.GetOrAdd(seqNum, _ => new CancellationTokenSource());

                    await _rxQueue.EnqueueAsync((seqNum, packet));
                    break;

                case MessageType.Response:

                    // We did not already receive a response for this corr-id.
                    if (_responseTable.TryRemove(corrId, out responseSource))
                    {
                        responseSource.SetResult((message, handled));
                    }
                    break;

                case MessageType.CancellationRequest:
                    if (_cancellationTable.TryGetValue(corrId, out var cancellationSource))
                    {
                        cancellationSource.Cancel();
                    }
                    break;

                case MessageType.CancellationResponse:
                    // We did not already receive a response for this corr-id.
                    if (_responseTable.TryGetValue(corrId, out responseSource))
                    {
                        responseSource.TrySetCanceled();
                    }
                    break;

                default:
                    // await SendBadMessageAsync(address, seqNum);
                    break;
            }
        }

        #endregion

        #region IRequestReplyEndPoint

        public async Task<(IMessage message, bool handled)> SendAsync(TPacket packet, CancellationToken cancellation = default)
        {
            if (packet == default)
            {
                throw new ArgumentDefaultException(nameof(packet));
            }

            if (packet.Message == null)
            {
                throw new ArgumentException("The packets message must not be null.");
            }

            using (CheckDisposal(ref cancellation, out var externalCancellation, out var disposal))
            {
                try
                {
                    var responseSource = new TaskCompletionSource<(IMessage response, bool handled)>();
                    var seqNum = GetNextSeqNum();

                    while (!_responseTable.TryAdd(seqNum, responseSource))
                    {
                        seqNum = GetNextSeqNum();
                    }

                    void RequestCancellation()
                    {
                        SendInternalAsync(packet.WithMessage(new Message()), messageType: MessageType.CancellationRequest, handled: false, corrId: seqNum, cancellation: default)
                            .HandleExceptions(_logger);
                    }

                    cancellation.ThrowIfCancellationRequested();

                    using (cancellation.Register(RequestCancellation))
                    {
                        await SendInternalAsync(packet, seqNum, MessageType.Request, handled: false, corrId: 0, cancellation: cancellation);

                        // The tasks gets cancelled if cancellation is requested and we receive a cancellation response from the client.
                        return await responseSource.Task;
                    }
                }
                catch (OperationCanceledException) when (disposal.IsCancellationRequested)
                {
                    throw new ObjectDisposedException(GetType().FullName);
                }
            }
        }

        public async Task<IMessageReceiveResult<TPacket>> ReceiveAsync(CancellationToken cancellation = default)
        {
            using (CheckDisposal(ref cancellation, out var externalCancellation, out var disposal))
            {
                try
                {
                    var (seqNum, packet) = await _rxQueue.DequeueAsync(cancellation);
                    return new MessageReceiveResult(this, seqNum, packet);
                }
                catch (OperationCanceledException) when (disposal.IsCancellationRequested)
                {
                    throw new ObjectDisposedException(GetType().FullName);
                }
            }
        }

        #endregion

        #region Disposal

        public void Dispose()
        {
            var disposalSource = Interlocked.Exchange(ref _disposalSource, null);
            if (disposalSource != null)
            {
                disposalSource.Cancel();
                _receiveProcess.Terminate();
                disposalSource.Dispose();
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

        #region Send

        private async Task SendInternalAsync(TPacket packet, int seqNum, MessageType messageType, bool handled, int corrId, CancellationToken cancellation)
        {
            var message = packet.Message;

            if (message == null)
            {
                message = new Message();
                packet = packet.WithMessage(message);
            }

            message = message ?? new Message();
            var frameIdx = message.FrameIndex;
            EncodeMessage(message, seqNum, messageType, handled, corrId);

            try
            {
                await _endPoint.SendAsync(packet, cancellation);
            }
            catch
            {
                if (frameIdx != message.FrameIndex)
                {
                    message.PopFrame();
                }

                Assert(frameIdx == message.FrameIndex);

                throw;
            }
        }

        private Task SendInternalAsync(TPacket packet, MessageType messageType, bool handled, int corrId, CancellationToken cancellation)
        {
            return SendInternalAsync(packet, GetNextSeqNum(), messageType, handled, corrId, cancellation);
        }

        #endregion

        private int GetNextSeqNum()
        {
            return Interlocked.Increment(ref _nextSeqNum);
        }

        // TODO: Use SpanReader/Writer API
        private static (int seqNum, MessageType messageType, bool handled, int corrId) DecodeMessage(IMessage message)
        {
            using (var stream = message.PopFrame().OpenStream())
            using (var reader = new BinaryReader(stream))
            {
                var seqNum = reader.ReadInt32();
                var messageType = (MessageType)reader.ReadInt32();
                var handled = reader.ReadBoolean();
                reader.ReadInt16();
                reader.ReadByte();
                var corrId = reader.ReadInt32();

                return (seqNum, messageType, handled, corrId);
            }
        }

        private static void EncodeMessage(IMessage message, int seqNum, MessageType messageType, bool handled, int corrId)
        {
            using (var stream = message.PushFrame().OpenStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(seqNum);           // 4 bytes
                writer.Write((int)messageType); // 4 bytes
                writer.Write(handled);          // 1 byte
                writer.Write((short)0);
                writer.Write((byte)0);          // 3 bytes (padding)
                writer.Write(corrId);           // 4 bytes
            }
        }

        private sealed class MessageReceiveResult : IMessageReceiveResult<TPacket>
        {
            private readonly RequestReplyEndPoint<TPacket> _rqRplyEndPoint;
            private readonly int _seqNum;
            private readonly CancellationTokenSource _cancellationRequestSource;

            public MessageReceiveResult(RequestReplyEndPoint<TPacket> rqRplyEndPoint, int seqNum, TPacket packet)
            {
                _rqRplyEndPoint = rqRplyEndPoint;
                _seqNum = seqNum;
                Packet = packet;

                _cancellationRequestSource = _rqRplyEndPoint._cancellationTable.GetOrAdd(seqNum, new CancellationTokenSource());
            }

            public CancellationToken Cancellation => _cancellationRequestSource.Token;

            public IMessage Message => Packet.Message;

            public TPacket Packet { get; }

            public Task SendResponseAsync(IMessage response)
            {
                return SendResponseAsync(response, handled: true);
            }

            public Task SendResponseAsync(IMessage response, bool handled)
            {
                if (response == null)
                {
                    throw new ArgumentNullException(nameof(response));
                }

                return InternalSendResponseAsync(response, handled);
            }

            public Task SendAckAsync()
            {
                // The handled parameter can be of any value here, as it is ignored by the receiver currently.
                return InternalSendResponseAsync(response: null, handled: false);
            }

            public Task SendCancellationAsync()
            {
                // The handled parameter can be of any value here, as it is ignored by the receiver currently.
                return _rqRplyEndPoint.SendInternalAsync(
                    Packet.WithMessage(new Message()),
                    messageType: MessageType.CancellationResponse,
                    handled: false,
                    corrId: _seqNum,
                    cancellation: default);
            }

            private Task InternalSendResponseAsync(IMessage response, bool handled)
            {
                return _rqRplyEndPoint.SendInternalAsync(
                    Packet.WithMessage(response),
                    MessageType.Response,
                    handled,
                    corrId: _seqNum,
                    cancellation: Cancellation);
            }

            public void Dispose()
            {
                _cancellationRequestSource.Dispose();
                _rqRplyEndPoint._cancellationTable.Remove(_seqNum, _cancellationRequestSource);
            }
        }

        private enum MessageType
        {
            CancellationRequest,
            CancellationResponse,
            Request,
            Response
        }
    }
}
