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

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using AI4E.Utils;
using Newtonsoft.Json;
using static System.Diagnostics.Debug;

namespace AI4E
{
    // This is inspired by the ViewDataDictionary, Asp.Net Core MVC uses to pass the view data from the controller to the view.
    // We have to use an immutable type however, to ensure consistency, as our messaging solution is not guaranteed to be used
    // by a single thread only.
    [JsonConverter(typeof(DispatchDataDictionaryConverter))]
    public abstract class DispatchDataDictionary : IReadOnlyDictionary<string, object>
    {
        private static readonly Type _dispatchDataDictionaryTypeDefinition = typeof(DispatchDataDictionary<>);
        private static readonly ConcurrentDictionary<Type, Func<object, IEnumerable<KeyValuePair<string, object>>, DispatchDataDictionary>> _factories
            = new ConcurrentDictionary<Type, Func<object, IEnumerable<KeyValuePair<string, object>>, DispatchDataDictionary>>();

        public static DispatchDataDictionary Create(Type messageType, object message)
        {
            return Create(messageType, message, ImmutableDictionary<string, object>.Empty);
        }

        public static DispatchDataDictionary Create(Type messageType, object message, IEnumerable<KeyValuePair<string, object>> data)
        {
            ValidateArguments(messageType, message, data);

            var factory = _factories.GetOrAdd(messageType, BuildFactory);
            return factory(message, data);
        }

        private static Func<object, IEnumerable<KeyValuePair<string, object>>, DispatchDataDictionary> BuildFactory(Type messageType)
        {
            var dispatchDataDictionaryType = _dispatchDataDictionaryTypeDefinition.MakeGenericType(messageType);

            Assert(dispatchDataDictionaryType != null);

            var ctor = dispatchDataDictionaryType.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public,
                Type.DefaultBinder,
                new Type[] { messageType, typeof(IEnumerable<KeyValuePair<string, object>>) },
                modifiers: null);

            Assert(ctor != null);

            var messageParameter = Expression.Parameter(typeof(object), "message");
            var dataParameter = Expression.Parameter(typeof(IEnumerable<KeyValuePair<string, object>>), "data");
            var convertedMessage = Expression.Convert(messageParameter, messageType);
            var ctorCall = Expression.New(ctor, convertedMessage, dataParameter);
            var convertedResult = Expression.Convert(ctorCall, typeof(DispatchDataDictionary));
            var lambda = Expression.Lambda<Func<object, IEnumerable<KeyValuePair<string, object>>, DispatchDataDictionary>>(
                convertedResult,
                messageParameter,
                dataParameter);

            return lambda.Compile();

        }

        private readonly ImmutableDictionary<string, object> _data;

        #region C'tor

        // We cannot add a public constructor here. As the type is not sealed and cannot be (the generic version inherits from this type)
        // anyone could inherit from the type. We cannot ensure immutability in this case.
        // Normally this type is not created directly anyway but an instance of the derived (generic type is used) and this type is used 
        // only as cast target, if we do not know the message type.
        private protected DispatchDataDictionary(Type messageType, object message, IEnumerable<KeyValuePair<string, object>> data)
        {
            ValidateArguments(messageType, message, data);

            MessageType = messageType;
            Message = message;
            _data = data.ToImmutableDictionary();
        }

        private static void ValidateArguments(Type messageType, object message, IEnumerable<KeyValuePair<string, object>> data)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            if (messageType == null)
                throw new ArgumentNullException(nameof(messageType));

            if (data == null)
                throw new ArgumentNullException(nameof(data));

            if (messageType.IsValueType)
                throw new ArgumentException("The argument must specify a reference type.", nameof(messageType));

            if (messageType.IsDelegate())
                throw new ArgumentException("The argument must not specify a delegate type.", nameof(messageType));

            if (messageType.IsGenericTypeDefinition)
                throw new ArgumentException("The argument must not be an open generic type definition.", nameof(messageType));

            // TODO: Do we have to check for System.Void? It is defined as a value type, that we alread check for.
            if (messageType == typeof(Enum) || messageType == typeof(ValueType) || messageType == typeof(void))
                throw new ArgumentException("The argument must not be one of the special types 'System.Enum', 'System.ValueType' or 'System.Void'.", nameof(messageType));

            if (!messageType.IsAssignableFrom(message.GetType()))
                throw new ArgumentException($"The specified message must be of type '{ messageType }' or a derived type.");

            // Altough we already checked whether message Type is neither a value type nor a delegate, 
            // it is possible that messageType is System.Object and the message is a delegate or a value type.
            if (messageType == typeof(object))
            {
                var actualMessageType = message.GetType();
                if (actualMessageType.IsValueType)
                {
                    throw new ArgumentException("The argument must be a reference type.", nameof(message));
                }

                if (actualMessageType.IsDelegate())
                {
                    throw new ArgumentException("The argument must not be a delegate.", nameof(message));
                }
            }
        }

        #endregion

        public Type MessageType { get; }
        public object Message { get; }

        #region IReadOnlyDictionary<string, object>

        public object this[string key]
        {
            get
            {
                // Do not pass through to _data as we do not want to throw a KeyNotFoundException
                if (key == null || _data == null)
                {
                    return null;
                }

                if (!_data.TryGetValue(key, out var result))
                {
                    result = null;
                }

                return result;
            }
        }

        public IEnumerable<string> Keys => _data?.Keys ?? Enumerable.Empty<string>();

        public IEnumerable<object> Values => _data?.Values ?? Enumerable.Empty<object>();

        public int Count => _data?.Count ?? 0;

        public bool ContainsKey(string key)
        {
            return key != null && _data != null && _data.ContainsKey(key);
        }

        public bool TryGetValue(string key, out object value)
        {
            if (key == null || _data == null)
            {
                value = default;
                return false;
            }

            return _data.TryGetValue(key, out value);
        }

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
        {
            var enumerable = _data as IEnumerable<KeyValuePair<string, object>> ?? Enumerable.Empty<KeyValuePair<string, object>>();

            return enumerable.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        #endregion
    }

    [JsonConverter(typeof(DispatchDataDictionaryConverter))]
    public sealed class DispatchDataDictionary<TMessage> : DispatchDataDictionary
        where TMessage : class
    {
        public DispatchDataDictionary(TMessage message, IEnumerable<KeyValuePair<string, object>> data)
            : base(typeof(TMessage), message, data)
        { }

        public DispatchDataDictionary(TMessage message)
            : base(typeof(TMessage), message, ImmutableDictionary<string, object>.Empty)
        { }

        public new TMessage Message => (TMessage)base.Message;
    }

    public sealed class DispatchDataDictionaryConverter : JsonConverter
    {
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (!(value is DispatchDataDictionary dispatchData))
            {
                writer.WriteNull();
                return;
            }

            writer.WriteStartObject();

            // Write message type
            writer.WritePropertyName("message-type");
            writer.WriteValue(dispatchData.MessageType.GetUnqualifiedTypeName());

            // Write message
            writer.WritePropertyName("message");
            serializer.Serialize(writer, dispatchData.Message, typeof(object));

            // Write data
            if (dispatchData.Any())
            {
                writer.WritePropertyName("data");
                writer.WriteStartObject();

                foreach (var kvp in dispatchData)
                {
                    writer.WritePropertyName(kvp.Key);
                    serializer.Serialize(writer, kvp.Value, typeof(object));
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (!CanConvert(objectType))
                throw new InvalidOperationException();

            if (reader.TokenType == JsonToken.Null)
                return null;

            if (reader.TokenType != JsonToken.StartObject)
                throw new InvalidOperationException();

            var messageType = objectType.IsGenericTypeDefinition ? objectType.GetGenericArguments().First() : null;
            object message = null;
            ImmutableDictionary<string, object>.Builder data = null;

            while (reader.Read())
            {
                if (reader.TokenType == JsonToken.EndObject)
                {
                    break;
                }

                else if (reader.TokenType == JsonToken.PropertyName)
                {
                    if ((string)reader.Value == "message-type")
                    {
                        reader.Read();
                        var deserializedMessageType = TypeLoadHelper.LoadTypeFromUnqualifiedName(reader.Value as string);

                        if (messageType != null && messageType != deserializedMessageType)
                        {
                            throw new InvalidOperationException();
                        }

                        messageType = deserializedMessageType;
                    }
                    else if ((string)reader.Value == "message")
                    {
                        reader.Read();
                        message = serializer.Deserialize(reader, typeof(object));
                    }
                    else if ((string)reader.Value == "data")
                    {
                        data = ReadData(reader, serializer);
                    }
                    else
                    {
                        throw new InvalidOperationException();
                    }
                }
                else
                {
                    throw new InvalidOperationException();
                }
            }

            if (messageType == null || message == null)
                throw new InvalidOperationException();

            return DispatchDataDictionary.Create(messageType, message, data?.ToImmutable() ?? ImmutableDictionary<string, object>.Empty);
        }

        private ImmutableDictionary<string, object>.Builder ReadData(JsonReader reader, JsonSerializer serializer)
        {
            var result = ImmutableDictionary.CreateBuilder<string, object>();

            if (!reader.Read() || reader.TokenType != JsonToken.StartObject)
                return null;

            while (reader.Read())
            {
                if (reader.TokenType == JsonToken.EndObject)
                {
                    return result;
                }
                else if (reader.TokenType == JsonToken.PropertyName)
                {
                    var key = (string)reader.Value;
                    reader.Read();
                    var value = serializer.Deserialize(reader, typeof(object));
                }
                else
                {
                    throw new InvalidOperationException();
                }
            }

            return result;
        }

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(DispatchDataDictionary) ||
                   objectType.IsGenericType &&
                   objectType.GetGenericTypeDefinition() == typeof(DispatchDataDictionary<>);
        }
    }
}
