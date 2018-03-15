/* Summary
 * --------------------------------------------------------------------------------------------------------------------
 * Filename:        ICommit.cs 
 * Types:           (1) AI4E.Storage.ICommit
 * Version:         1.0
 * Author:          Andreas Trütschel
 * Last modified:   04.01.2018 
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
 * NEventStore (https://github.com/NEventStore/NEventStore)
 * The MIT License
 * 
 * Copyright (c) 2013 Jonathan Oliver, Jonathan Matheus, Damian Hickey and contributors
 * 
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 * --------------------------------------------------------------------------------------------------------------------
 */

using System;
using System.Collections.Generic;

namespace AI4E.Storage
{
    /// <summary>
    /// Represents a series of events which have been fully committed as a single unit and which apply to the stream indicated.
    /// </summary>
    public interface ICommit<TBucket, TStreamId>
        where TBucket : IEquatable<TBucket>
        where TStreamId : IEquatable<TStreamId>
    {
        /// <summary>
        /// Gets the value which identifies bucket to which the the stream and the the commit belongs.
        /// </summary>
        TBucket BucketId { get; }

        /// <summary>
        /// Gets the value which uniquely identifies the stream to which the commit belongs.
        /// </summary>
        TStreamId StreamId { get; }

        /// <summary>
        /// Gets the value which uniquely identifies the commit within the stream.
        /// </summary>
        Guid ConcurrencyToken { get; }

        /// <summary>
        /// Gets the value which indicates the sequence (or position) in the stream to which this commit applies.
        /// </summary>
        long StreamRevision { get; }

        /// <summary>
        /// Gets the point in time at which the commit was persisted.
        /// </summary>
        DateTime CommitStamp { get; }

        /// <summary>
        /// Gets the metadata which provides additional, unstructured information about this commit.
        /// </summary>
        IReadOnlyDictionary<string, object> Headers { get; }

        object Body { get; }

        /// <summary>
        /// Gets the collection of event messages to be committed as a single unit.
        /// </summary>
        IReadOnlyCollection<EventMessage> Events { get; }
    }
}