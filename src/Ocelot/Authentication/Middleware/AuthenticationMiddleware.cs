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
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Ocelot.Configuration;
using Ocelot.Logging;
using Ocelot.Middleware;

namespace Ocelot.Authentication.Middleware
{
    public class AuthenticationMiddleware : OcelotMiddleware
    {
        private readonly OcelotRequestDelegate _next;

        public AuthenticationMiddleware(OcelotRequestDelegate next,
            IOcelotLoggerFactory loggerFactory)
            : base(loggerFactory.CreateLogger<AuthenticationMiddleware>())
        {
            _next = next;
        }

        public async Task Invoke(DownstreamContext context)
        {
            if (context.HttpContext.Request.Method.ToUpper() != "OPTIONS" && IsAuthenticatedRoute(context.DownstreamReRoute))
            {
                Logger.LogInformation($"{context.HttpContext.Request.Path} is an authenticated route. {MiddlewareName} checking if client is authenticated");

                var result = await context.HttpContext.AuthenticateAsync(context.DownstreamReRoute.AuthenticationOptions.AuthenticationProviderKey);

                context.HttpContext.User = result.Principal;

                if (context.HttpContext.User.Identity.IsAuthenticated)
                {
                    Logger.LogInformation($"Client has been authenticated for {context.HttpContext.Request.Path}");
                    await _next.Invoke(context);
                }
                else
                {
                    var error = new UnauthenticatedError(
                        $"Request for authenticated route {context.HttpContext.Request.Path} by {context.HttpContext.User.Identity.Name} was unauthenticated");

                    Logger.LogWarning($"Client has NOT been authenticated for {context.HttpContext.Request.Path} and pipeline error set. {error}");

                    SetPipelineError(context, error);
                }
            }
            else
            {
                Logger.LogInformation($"No authentication needed for {context.HttpContext.Request.Path}");

                await _next.Invoke(context);
            }
        }

        private static bool IsAuthenticatedRoute(DownstreamReRoute reRoute)
        {
            return reRoute.IsAuthenticated;
        }
    }
}
