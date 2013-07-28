using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NowinWebServer
{
    using OwinApp = Func<IDictionary<string, object>, Task>;

    public class OwinHandler : IHttpLayerHandler
    {
        readonly OwinApp _app;

        readonly OwinEnvironment _environment;
        internal readonly Dictionary<string, string[]> ReqHeaders;
        internal readonly Dictionary<string, string[]> RespHeaders;

        public IHttpLayerCallback Callback { set; internal get; }

        public OwinHandler(OwinApp app)
        {
            _app = app;
            _environment = new OwinEnvironment(this);
            ReqHeaders = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            RespHeaders = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        }

        public void Dispose()
        {
        }

        public void PrepareForRequest()
        {
            _environment.Reset();
            ReqHeaders.Clear();
            RespHeaders.Clear();
        }

        public void AddRequestHeader(string name, string value)
        {
            string[] values;
            if (ReqHeaders.TryGetValue(name, out values))
            {
                Array.Resize(ref values, values.Length + 1);
                values[values.Length - 1] = value;
                ReqHeaders[name] = values;
            }
            else
            {
                ReqHeaders.Add(name, new[] { value });
            }
        }

        public void HandleRequest()
        {
            Callback.ResponseWriteIsFlushAndFlushIsNoOp = false;
            var task = _app(_environment);
            if (task.IsCompleted)
            {
                if (task.IsFaulted || task.IsCanceled)
                {
                    Callback.ResponseStatusCode = 500;
                    Callback.ResponseReasonPhase = null;
                }
                Callback.ResponseFinished();
                return;
            }
            Callback.ResponseWriteIsFlushAndFlushIsNoOp = true;
            task.ContinueWith((t, o) =>
                {
                    if (t.IsFaulted || t.IsCanceled)
                    {
                        Callback.ResponseStatusCode = 500;
                        Callback.ResponseReasonPhase = null;
                    }
                    Callback.ResponseFinished();
                    _environment.Reset();
                }, this);
        }

        int GetStatusFromEnvironment()
        {
            object value;
            if (!_environment.TryGetValue(OwinKeys.ResponseStatusCode, out value))
                return 200;
            return (int)value;
        }

        public void PrepareResponseHeaders()
        {
            Callback.ResponseStatusCode = GetStatusFromEnvironment();
            string[] connectionValues;
            var headers = (IDictionary<string, string[]>)_environment[OwinKeys.ResponseHeaders];
            if (headers == null)
            {
                RespHeaders.Clear();
                headers = RespHeaders;
            }
            object responsePhase;
            if (_environment.TryGetValue(OwinKeys.ResponseReasonPhrase, out responsePhase))
            {
                if (responsePhase != null)
                {
                    if (!(responsePhase is String))
                    {
                        Callback.ResponseStatusCode = 500;
                        return;
                    }
                    Callback.ResponseReasonPhase = (string) responsePhase;
                }
            }
            if (headers.TryGetValue("Connection", out connectionValues))
            {
                headers.Remove("Connection");
                if (connectionValues.Length != 1)
                {
                    Callback.ResponseStatusCode = 500;
                    return;
                }
                var v = connectionValues[0];
                if (v.Equals("Close", StringComparison.InvariantCultureIgnoreCase))
                    Callback.KeepAlive = false;
                else if (v.Equals("Keep-alive", StringComparison.InvariantCultureIgnoreCase))
                    Callback.KeepAlive = true;
            }
            string[] contentLengthValues;
            if (headers.TryGetValue("Content-Length", out contentLengthValues))
            {
                headers.Remove("Content-Length");
                if (contentLengthValues.Length != 1)
                {
                    Callback.ResponseStatusCode = 500;
                    return;
                }
                ulong temp;
                if (!ulong.TryParse(contentLengthValues[0], out temp))
                {
                    Callback.ResponseStatusCode = 500;
                    return;
                }
                Callback.ResponseContentLength = temp;
            }
            headers.Remove("Transfer-Encoding");
            foreach (var header in headers)
            {
                Callback.AddResponseHeader(header.Key, header.Value);
            }
        }
    }
}