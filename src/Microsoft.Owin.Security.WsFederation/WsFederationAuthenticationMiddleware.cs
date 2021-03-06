﻿// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved. See License.txt in the project root for license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using Microsoft.Owin.Logging;
using Microsoft.Owin.Security.DataHandler;
using Microsoft.Owin.Security.DataProtection;
using Microsoft.Owin.Security.Infrastructure;
using Owin;

namespace Microsoft.Owin.Security.WsFederation
{
    /// <summary>
    /// OWIN middleware for obtaining identities using WsFederation protocol.
    /// </summary>
    [SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable", Justification = "Middleware are not disposable.")]
    public class WsFederationAuthenticationMiddleware : AuthenticationMiddleware<WsFederationAuthenticationOptions>
    {
        private readonly ILogger _logger;
        private readonly HttpClient _httpClient;

        /// <summary>
        /// Initializes a <see cref="WsFederationAuthenticationMiddleware"/>
        /// </summary>
        /// <param name="next">The next middleware in the OWIN pipeline to invoke</param>
        /// <param name="app">The OWIN application</param>
        /// <param name="options">Configuration options for the middleware</param>
        public WsFederationAuthenticationMiddleware(OwinMiddleware next, IAppBuilder app, WsFederationAuthenticationOptions options)
            : base(next, options)
        {
            _logger = app.CreateLogger<WsFederationAuthenticationMiddleware>();

            // if (string.IsNullOrEmpty(Options.SignInAsAuthenticationType))
            // {
            //     Options.SignInAsAuthenticationType = app.GetDefaultSignInAsAuthenticationType();
            // }

            if (Options.StateDataFormat == null)
            {
                var dataProtector = app.CreateDataProtector(
                    typeof(WsFederationAuthenticationMiddleware).FullName,
                    Options.AuthenticationType, "v1");
                Options.StateDataFormat = new PropertiesDataFormat(dataProtector);
            }

            _httpClient = new HttpClient(ResolveHttpMessageHandler(Options));
            _httpClient.Timeout = Options.BackchannelTimeout;
            _httpClient.MaxResponseContentBufferSize = 1024 * 1024 * 10; // 10 MB
        }

        /// <summary>
        /// Provides the <see cref="AuthenticationHandler"/> object for processing authentication-related requests.
        /// </summary>
        /// <returns>An <see cref="AuthenticationHandler"/> configured with the <see cref="WsFederationAuthenticationOptions"/> supplied to the constructor.</returns>
        [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "By design, backchannel errors should not be shown to the request.")]
        protected override AuthenticationHandler<WsFederationAuthenticationOptions> CreateHandler()
        {
            if (!Options.TokenValidationParameters.AreIssuerSigningKeysAvailable && Options.MetadataAddress != null)
            {
                try
                {
                    IssuerFederationData federationData = WsFedMetadataRetriever.GetFederationMetataData(Options.MetadataAddress, _httpClient);
                    if (federationData.IssuerSigningTokens != null)
                    {
                        Options.TokenValidationParameters.IssuerSigningTokens = federationData.IssuerSigningTokens;
                        Options.IssuerAddress = federationData.PassiveTokenEndpoint;
                        Options.TokenValidationParameters.ValidIssuer = federationData.TokenIssuerName;
                    }
                }
                catch (Exception)
                {
                    // ignore any errors from getting signing keys
                }
            }

            return new WsFederationAuthenticationHandler(_logger);
        }

        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", Justification = "Managed by caller")]
        private static HttpMessageHandler ResolveHttpMessageHandler(WsFederationAuthenticationOptions options)
        {
            HttpMessageHandler handler = options.BackchannelHttpHandler ?? new WebRequestHandler();

            // If they provided a validator, apply it or fail.
            if (options.BackchannelCertificateValidator != null)
            {
                // Set the cert validate callback
                var webRequestHandler = handler as WebRequestHandler;
                if (webRequestHandler == null)
                {
                    throw new InvalidOperationException(Resources.Exception_ValidatorHandlerMismatch);
                }
                webRequestHandler.ServerCertificateValidationCallback = options.BackchannelCertificateValidator.Validate;
            }

            return handler;
        }
    }
}