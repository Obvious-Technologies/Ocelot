#region header
#pragma warning disable S125
/* ************************************************************************** */
/*  _____ _       Project Ocelot @ Obvious Technologies (c)                   */
/* |  _  | |        (_)                                                       */
/* | | | | |____   ___  ___  _   _ ___    Created: 03/01/2019 15:55:35        */
/* | | | | '_ \ \ / / |/ _ \| | | / __|                                       */
/* \ \_/ | |_) \ V /| | (_) | |_| \__ \   By:      Beranger Kabbas            */
/*  \___/|_.__/ \_/ |_|\___/ \__,_|___/            bkabbas@axonesys.com       */
/*       _____         _                 _             _                      */
/*      |_   _|       | |               | |           (_)                     */
/*        | | ___  ___| |__  _ __   ___ | | ___   __ _ _  ___ ___             */
/*        | |/ _ \/ __| '_ \| '_ \ / _ \| |/ _ \ / _  | |/ _ / __|            */
/*        | |  __/ (__| | | | | | | (_) | | (_) | (_| | |  __\__ \            */
/*        \_/\___|\___|_| |_|_| |_|\___/|_|\___/ \__  |_|\___|___/            */
/*                                                __/ |                       */
/*        https://obvious.tech                   |___/                        */
/* ************************************************************************** */
#pragma warning restore S125
#endregion header
using System;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Ocelot.Logging;
using Ocelot.Middleware;

namespace Ocelot.WebSockets.Middleware
{
    public class WebSocketsProxyMiddleware : OcelotMiddleware
    {
        private static readonly string[] NotForwardedWebSocketHeaders = new[] { "Connection", "Host", "Upgrade", "Sec-WebSocket-Accept", "Sec-WebSocket-Protocol", "Sec-WebSocket-Key", "Sec-WebSocket-Version", "Sec-WebSocket-Extensions" };
        private const int DefaultWebSocketBufferSize = 4096;
        private const int StreamCopyBufferSize = 81920;
        private readonly OcelotRequestDelegate _next;

        public WebSocketsProxyMiddleware(OcelotRequestDelegate next,
            IOcelotLoggerFactory loggerFactory)
                : base(loggerFactory.CreateLogger<WebSocketsProxyMiddleware>())
        {
            ;
            _next = next;
        }

        private static async Task PumpWebSocket(WebSocket source, WebSocket destination, int bufferSize, CancellationToken cancellationToken)
        {
            if (bufferSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bufferSize));
            }

            var buffer = new byte[bufferSize];
            while (true)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await source.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    await destination.CloseOutputAsync(WebSocketCloseStatus.EndpointUnavailable, null, cancellationToken);
                    return;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await destination.CloseOutputAsync(source.CloseStatus.Value, source.CloseStatusDescription, cancellationToken);
                    return;
                }

                await destination.SendAsync(new ArraySegment<byte>(buffer, 0, result.Count), result.MessageType, result.EndOfMessage, cancellationToken);
            }
        }

        public async Task Invoke(DownstreamContext context)
        {
            await Proxy(context.HttpContext, context.DownstreamRequest.ToUri());
        }

        private async Task Proxy(HttpContext context, string serverEndpoint)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (serverEndpoint == null)
            {
                throw new ArgumentNullException(nameof(serverEndpoint));
            }

            if (!context.WebSockets.IsWebSocketRequest)
            {
                throw new InvalidOperationException();
            }

            var client = new ClientWebSocket();
            foreach (var protocol in context.WebSockets.WebSocketRequestedProtocols)
            {
                client.Options.AddSubProtocol(protocol);
            }

            foreach (var headerEntry in context.Request.Headers)
            {
                if (!NotForwardedWebSocketHeaders.Contains(headerEntry.Key, StringComparer.OrdinalIgnoreCase))
                {
                    client.Options.SetRequestHeader(headerEntry.Key, headerEntry.Value);
                }
            }

            if (serverEndpoint.StartsWith("http"))
                serverEndpoint = "ws" + serverEndpoint.Substring("http".Length);

            var destinationUri = new Uri(serverEndpoint);
            Logger.LogDebug(serverEndpoint);
            await client.ConnectAsync(destinationUri, context.RequestAborted);
            using (var server = await context.WebSockets.AcceptWebSocketAsync(client.SubProtocol))
            {
                var bufferSize = DefaultWebSocketBufferSize;
                await Task.WhenAll(PumpWebSocket(client, server, bufferSize, context.RequestAborted), PumpWebSocket(server, client, bufferSize, context.RequestAborted));
            }
        }
    }
}
