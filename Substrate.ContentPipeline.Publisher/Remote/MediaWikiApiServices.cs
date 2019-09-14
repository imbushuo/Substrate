// Copyright 2019 The Lawrence Industry and its affiliates. All rights reserved.
//
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Principal;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Substrate.ContentPipeline.Publisher.Configuration;

namespace Substrate.ContentPipeline.Publisher.Remote
{
    public class MediaWikiApiServices
    {
        private readonly ILogger _logger;
        private readonly IOptions<ApiCredentials> _cred;

        private CookieContainer _cookieContainer;

        public IPrincipal CurrentIdentity { get; private set; }

        public MediaWikiApiServices(
            IOptions<ApiCredentials> credentials,
            ILogger<MediaWikiApiServices> logger)
        {
            _cred = credentials;
            _logger = logger;

            _cookieContainer = new CookieContainer();
        }

        private async Task<string> GetLoginTokenAsync()
        {
            var apiParams = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("action", "query"),
                new KeyValuePair<string, string>("meta", "tokens"),
                new KeyValuePair<string, string>("type", "login"),
                new KeyValuePair<string, string>("format", "json"),
                new KeyValuePair<string, string>("formatversion", "2")
            };

            using (var httpHandler = new HttpClientHandler { CookieContainer = _cookieContainer, UseCookies = true })
            using (var httpClient = new HttpClient(httpHandler))
            using (var apiContent = new FormUrlEncodedContent(apiParams))
            using (var apiResult = await httpClient.PostAsync( _cred.Value.Endpoint, apiContent))
            {
                apiResult.EnsureSuccessStatusCode();
                dynamic apiResponse = JsonConvert.DeserializeObject(await apiResult.Content.ReadAsStringAsync());
                return (string) apiResponse.query.tokens.logintoken;
            }
        }

        public async Task<IPrincipal> LoginAsync()
        {
            // Previous identity must be invalidated
            CurrentIdentity = null;

            try
            {
                var apiParams = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("action", "login"),
                    new KeyValuePair<string, string>("lgname", _cred.Value.Username),
                    new KeyValuePair<string, string>("lgpassword", _cred.Value.Password),
                    new KeyValuePair<string, string>("lgtoken", await GetLoginTokenAsync()),
                    new KeyValuePair<string, string>("format", "json"),
                    new KeyValuePair<string, string>("formatversion", "2")
                };

                using (var httpHandler = new HttpClientHandler { CookieContainer = _cookieContainer, UseCookies = true })
                using (var httpClient = new HttpClient(httpHandler))
                using (var apiContent = new FormUrlEncodedContent(apiParams))
                using (var apiResult = await httpClient.PostAsync(_cred.Value.Endpoint, apiContent))
                {
                    apiResult.EnsureSuccessStatusCode();
                    dynamic apiResponse = JsonConvert.DeserializeObject(await apiResult.Content.ReadAsStringAsync());
                    if (((string)apiResponse.login.result) != "Success")
                    {
                        throw new InvalidOperationException($"Remote login failed: {apiResponse.login.result}");
                    }

                    CurrentIdentity = new GenericPrincipal(
                        new GenericIdentity((string) apiResponse.login.lgusername,
                        _cred.Value.Endpoint), new string[] { });
                }
            }
            catch (Exception exc)
            {
                _logger.LogError(exc, "MW client login failed");
            }

            return CurrentIdentity;
        }
    }
}
