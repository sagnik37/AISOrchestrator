using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Claims;

namespace Rpc.AIS.Accrual.Orchestrator.Tests.TestDoubles;

/// <summary>
/// Provides fake http request data behavior.
/// </summary>
internal sealed class FakeHttpRequestData : HttpRequestData
{
    private readonly Uri _url;

    public FakeHttpRequestData(FunctionContext functionContext, Uri url, Stream body)
        : base(functionContext)
    {
        _url = url ?? throw new ArgumentNullException(nameof(url));
        Body = body ?? throw new ArgumentNullException(nameof(body));

        Headers = new HttpHeadersCollection();
        Cookies = Array.Empty<IHttpCookie>();
        Identities = Array.Empty<ClaimsIdentity>();
        Method = "POST";
    }

    public override Stream Body { get; }

    public override HttpHeadersCollection Headers { get; }

    public override IReadOnlyCollection<IHttpCookie> Cookies { get; }

    public override Uri Url => _url;

    public override IEnumerable<ClaimsIdentity> Identities { get; }

    public override string Method { get; }

    /// <summary>
    /// Executes create response.
    /// </summary>
    public override HttpResponseData CreateResponse()
        => new FakeHttpResponseData(FunctionContext);
}

/// <summary>
/// Provides fake http response data behavior.
/// </summary>
internal sealed class FakeHttpResponseData : HttpResponseData
{
    private readonly HttpCookies _cookies = new FakeHttpCookies();

    public FakeHttpResponseData(FunctionContext functionContext)
        : base(functionContext)
    {
        Headers = new HttpHeadersCollection();
        Body = new MemoryStream();
        StatusCode = HttpStatusCode.OK;
    }

    public override HttpStatusCode StatusCode { get; set; }

    public override HttpHeadersCollection Headers { get; set; }

    public override Stream Body { get; set; }

    public override HttpCookies Cookies => _cookies;

    /// <summary>
    /// Provides fake http cookies behavior.
    /// </summary>
    private sealed class FakeHttpCookies : HttpCookies
    {
        private readonly List<IHttpCookie> _items = new();

        /// <summary>
        /// Executes create new.
        /// </summary>
        public override IHttpCookie CreateNew()
        {
            var cookie = new SimpleHttpCookie(name: string.Empty, value: string.Empty);
            _items.Add(cookie);
            return cookie;
        }

        /// <summary>
        /// Executes append.
        /// </summary>
        public override void Append(IHttpCookie cookie)
        {
            if (cookie is null) throw new ArgumentNullException(nameof(cookie));
            _items.Add(cookie);
        }

        /// <summary>
        /// Executes append.
        /// </summary>
        public override void Append(string name, string value)
        {
            _items.Add(new SimpleHttpCookie(name, value));
        }

        /// <summary>
        /// Provides simple http cookie behavior.
        /// </summary>
        private sealed class SimpleHttpCookie : IHttpCookie
        {
            public SimpleHttpCookie(string name, string value)
            {
                Name = name ?? throw new ArgumentNullException(nameof(name));
                Value = value ?? string.Empty;
            }

            public string Name { get; }

            public string Value { get; set; }

            public string? Domain { get; set; }

            public string? Path { get; set; }

            public DateTimeOffset? Expires { get; set; }

            public bool? Secure { get; set; }

            public bool? HttpOnly { get; set; }

            // FIX: must match interface type exactly: SameSite (non-nullable)
            public SameSite SameSite { get; set; }

            public double? MaxAge { get; set; }
        }
    }
}
