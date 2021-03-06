﻿/* Summary
 * --------------------------------------------------------------------------------------------------------------------
 * Filename:        IEndPointManager.cs 
 * Types:           (1) AI4E.Routing.IEndPointManager
 *                  (2) AI4E.Routing.IEndPointManager'1
 * Version:         1.0
 * Author:          Andreas Trütschel
 * Last modified:   10.05.2018 
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

using System;

namespace AI4E.Routing
{
    public interface IEndPointManager : IDisposable
    {
        ILogicalEndPoint GetLogicalEndPoint(EndPointAddress endPoint);
        ILogicalEndPoint CreateLogicalEndPoint(EndPointAddress endPoint);
    }

    public interface IEndPointManager<TAddress> : IEndPointManager, IDisposable
    {
        TAddress LocalAddress { get; }

        new ILogicalEndPoint<TAddress> GetLogicalEndPoint(EndPointAddress endPoint);
        new ILogicalEndPoint<TAddress> CreateLogicalEndPoint(EndPointAddress endPoint);
    }
}