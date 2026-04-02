// File: FscmAccountingPeriodResolver.Cache.cs
// split helper responsibilities into partial files (behavior preserved).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Rpc.AIS.Accrual.Orchestrator.Core.Abstractions;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain;
using Rpc.AIS.Accrual.Orchestrator.Core.Domain.Delta;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Options;
using Rpc.AIS.Accrual.Orchestrator.Infrastructure.Utils;

namespace Rpc.AIS.Accrual.Orchestrator.Infrastructure.Clients;

public sealed partial class FscmAccountingPeriodResolver
{



    private void CacheOutOfWindow(DateOnly key, bool isClosed)
    {
        lock (_outOfWindowCacheLock)
        {
            if (_outOfWindowClosedCache.ContainsKey(key))
                return;

            _outOfWindowClosedCache[key] = isClosed;
            _outOfWindowCacheOrder.Enqueue(key);

            var max = _opt.OutOfWindowDateCacheSize <= 0 ? 512 : _opt.OutOfWindowDateCacheSize;
            while (_outOfWindowClosedCache.Count > max && _outOfWindowCacheOrder.Count > 0)
            {
                var old = _outOfWindowCacheOrder.Dequeue();
                _outOfWindowClosedCache.Remove(old);
            }
        }
    }
}
