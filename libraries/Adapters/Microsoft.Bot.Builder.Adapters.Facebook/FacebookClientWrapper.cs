﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Bot.Schema;
using Newtonsoft.Json;

namespace Microsoft.Bot.Builder.Adapters.Facebook
{
    public class FacebookClientWrapper
    {
        private readonly FacebookAdapterOptions _options;

        /// <summary>
        /// Initializes a new instance of the <see cref="FacebookClientWrapper"/> class.
        /// </summary>
        /// <param name="options">An object containing API credentials, a webhook verification token and other options.</param>
        public FacebookClientWrapper(FacebookAdapterOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>
        /// Call one of the Facebook APIs.
        /// </summary>
        /// <param name="path">Path to the API endpoint, for example `/me/messages`.</param>
        /// <param name="payload">An object to be sent as parameters to the API call..</param>
        /// <param name="method">HTTP method, for example POST, GET, DELETE or PUT.</param>
        /// <param name="cancellationToken">A cancellation token for the task.</param>
        /// <returns>A task representing the async operation.</returns>
        public virtual async Task<HttpResponseMessage> SendMessageAsync(string path, FacebookMessage payload, HttpMethod method = null, CancellationToken cancellationToken = default)
        {
            var proof = GetAppSecretProof();

            if (method == null)
            {
                method = HttpMethod.Post;
            }

            // send the request
            using (var request = new HttpRequestMessage())
            {
                request.RequestUri = new Uri($"https://{_options.ApiHost}/{_options.ApiVersion + path}?access_token={_options.AccessToken}&appsecret_proof={proof}");
                request.Method = method;
                request.Content = new StringContent(JsonConvert.SerializeObject(payload));
                request.Content.Headers.Add("Content-Type", "application/json");

                using (var client = new HttpClient())
                {
                    return await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Get a Facebook API client with the correct credentials based on the page identified in the incoming activity.
        /// This is used by many internal functions to get access to the Facebook API, and is exposed as `bot.api` on any BotWorker instances passed into Botkit handler functions.
        /// </summary>
        /// <param name="activity">An incoming message activity.</param>
        /// <returns>A Facebook API client.</returns>
        public virtual async Task<FacebookClientWrapper> GetAPIAsync(Activity activity)
        {
            if (activity == null)
            {
                throw new ArgumentNullException(nameof(activity));
            }

            if (!string.IsNullOrWhiteSpace(_options.AccessToken))
            {
                return new FacebookClientWrapper(new FacebookAdapterOptions(_options.VerifyToken, _options.AppSecret, _options.AccessToken));
            }

            if (string.IsNullOrWhiteSpace(activity.Recipient?.Id))
            {
                throw new Exception($"Unable to create API based on activity:{activity}");
            }

            var pageId = activity.Recipient.Id;

            if ((activity.ChannelData as dynamic)?.message != null && (activity.ChannelData as dynamic)?.message.is_echo)
            {
                pageId = activity.From.Id;
            }

            var token = await _options.GetAccessTokenForPageAsync(pageId).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(token))
            {
                throw new ArgumentException(nameof(token));
            }

            return new FacebookClientWrapper(new FacebookAdapterOptions(_options.VerifyToken, _options.AppSecret, token));
        }

        /// <summary>
        /// Verifies the SHA1 signature of the raw request payload before bodyParser parses it will abort parsing if signature is invalid, and pass a generic error to response.
        /// </summary>
        /// <param name="request">An Http request object.</param>
        /// <param name="stringifyBody">The request body.</param>
        /// <returns>The result of the comparison between the signature in the request and hashed body.</returns>
        public virtual bool VerifySignature(HttpRequest request, string stringifyBody)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var expected = request.Headers["x-hub-signature"].ToString().ToUpperInvariant();

            var payload = EncodeNonAsciiCharacters(stringifyBody);

#pragma warning disable CA5350 // Facebook uses SHA1 as cryptographic algorithm.
            using (var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(_options.AppSecret)))
            {
                hmac.Initialize();
                var hashArray = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
                var hash = $"SHA1={BitConverter.ToString(hashArray).Replace("-", string.Empty)}";

                return expected == hash;
            }
#pragma warning restore CA5350 // Facebook uses SHA1 as cryptographic algorithm.
        }

        /// <summary>
        /// Generate the app secret proof used to increase security on calls to the graph API.
        /// </summary>
        /// <returns>The app secret proof.</returns>
        public virtual string GetAppSecretProof()
        {
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.AppSecret)))
            {
                return hmac.ComputeHash(Encoding.UTF8.GetBytes(_options.AccessToken)).ToString();
            }
        }

        /// <summary>
        /// Verifies the VerifyToken from the message and if it matches the one configured, it sends back the challenge.
        /// </summary>
        /// <param name="request">An Http request object.</param>
        /// <param name="response">An Http response object.</param>
        /// <param name="cancellationToken">A cancellation token for the task.</param>
        /// <returns>A task representing the async operation.</returns>
        public virtual async Task VerifyWebhookAsync(HttpRequest request, HttpResponse response, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (response == null)
            {
                throw new ArgumentNullException(nameof(response));
            }

            var challenge = string.Empty;
            HttpStatusCode statusCode;

            if (request.Query["hub.verify_token"].Equals(_options.VerifyToken))
            {
                challenge = request.Query["hub.challenge"];
                statusCode = HttpStatusCode.OK;
            }
            else
            {
                statusCode = HttpStatusCode.Unauthorized;
            }

            await FacebookHelper.WriteAsync(response, statusCode, challenge, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Encodes the non ASCII characters of a string.
        /// </summary>
        /// <param name="value">The string to encode.</param>
        /// <returns>The resulting string.</returns>
        private string EncodeNonAsciiCharacters(string value)
        {
            var sb = new StringBuilder();
            foreach (var c in value)
            {
                if (c > 127)
                {
                    var encodedValue = "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture);
                    sb.Append(encodedValue);
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }
    }
}
