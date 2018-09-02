﻿/* License
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

using System.Collections.Generic;
using System.Threading.Tasks;

namespace AI4E.Storage.Projection
{
    public interface IProjection<TSource, TProjection>
    {
        ///// <summary>
        ///// Asynchronously projects the specified source object to a single projection result.
        ///// </summary>
        ///// <param name="source">The source object.</param>
        ///// <returns>
        ///// A value task representing the asynchronous operation.
        ///// </returns>
        //ValueTask<TProjection> ProjectAsync(TSource source);

        /// <summary>
        /// Asynchronously projects the specified source object.
        /// </summary>
        /// <param name="source">The source object.</param>
        /// <returns>
        /// An async enumerable that enumerates the projection results.
        /// </returns>
        IAsyncEnumerable<TProjection> ProjectMultipleAsync(TSource source);

        ///// <summary>
        ///// Gets a boolean value indicating whether the projection has multiple results
        ///// </summary>
        //bool MultipleResults { get; }
    }
}
