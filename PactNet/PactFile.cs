﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using PactNet.Validators;

namespace PactNet
{
    internal class PactFile
    {
        private readonly IPactProviderResponseValidator _responseValidator;

        private IList<PactInteraction> _interactions;

        public PactParty Provider { get; set; }
        public PactParty Consumer { get; set; }

        public IEnumerable<PactInteraction> Interactions
        {
            get { return _interactions; }
        }

        public dynamic Metadata { get; private set; }

        public PactFile()
        {
            _responseValidator = new PactProviderResponseValidator();

            Metadata = new
            {
                PactSpecificationVersion = "1.0.0"
            };
        }

        private readonly JsonSerializerSettings _jsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore
        };

        private static readonly Dictionary<HttpVerb, HttpMethod> HttpVerbMap = new Dictionary<HttpVerb, HttpMethod>
        {
            { HttpVerb.Get, HttpMethod.Get },
            { HttpVerb.Post, HttpMethod.Post },
            { HttpVerb.Put, HttpMethod.Put },
            { HttpVerb.Delete, HttpMethod.Delete },
            { HttpVerb.Head, HttpMethod.Head },
            { HttpVerb.Patch, new HttpMethod("PATCH") }
        };

        public void VerifyProvider(HttpClient client)
        {
            if (Interactions == null || !Interactions.Any())
            {
                return;
            }

            var interationNumber = 1;
            foreach (var interaction in Interactions)
            {
                Console.WriteLine("{0}) Verifying a Pact between {1} and {2} - {3}.", interationNumber, Consumer.Name, Provider.Name, interaction.Description);

                var request = new HttpRequestMessage(HttpVerbMap[interaction.Request.Method], interaction.Request.Path);

                if (interaction.Request.Body != null)
                {
                    //If there is a content-type header add it to the content
                    var jsonRequestBody = JsonConvert.SerializeObject(interaction.Request.Body, _jsonSettings);
                    StringContent content;
                    if (interaction.Request.Headers != null && interaction.Request.Headers.ContainsKey("Content-Type"))
                    {
                        // TODO: This is extremely icky. Need a better way of dealing with this. Also set content encoding
                        var contentType = interaction.Request.Headers["Content-Type"].Split(';');
                        content = new StringContent(jsonRequestBody, Encoding.UTF8, contentType[0]);
                    }
                    else
                    {
                        content = new StringContent(jsonRequestBody);
                    }

                    request.Content = content;
                }

                if (interaction.Request.Headers != null && interaction.Request.Headers.Any())
                {
                    foreach (var requestHeader in interaction.Request.Headers)
                    {
                        //Ignore any content headers, as they will be attached above if applicable
                        if (requestHeader.Key.Equals("Content-Type", StringComparison.InvariantCultureIgnoreCase))
                        {
                            continue;
                        }

                        request.Headers.Add(requestHeader.Key, requestHeader.Value);
                    }
                }

                var response = client.SendAsync(request).Result;

                var actualResponse = new PactProviderResponse
                {
                    Status = (int)response.StatusCode,
                    Headers = ConvertHeaders(response.Headers, response.Content.Headers)
                };

                var responseContent = response.Content.ReadAsStringAsync().Result;
                if (!String.IsNullOrEmpty(responseContent))
                {
                    actualResponse.Body = JsonConvert.DeserializeObject<dynamic>(responseContent);
                }

                _responseValidator.Validate(interaction.Response, actualResponse);

                interationNumber++;
            }
        }

        private Dictionary<string, string> ConvertHeaders(HttpResponseHeaders responseHeaders, HttpContentHeaders contentHeaders)
        {
            if ((responseHeaders == null || !responseHeaders.Any()) &&
                (contentHeaders == null || !contentHeaders.Any()))
            {
                return null;
            }

            var headers = new Dictionary<string, string>();

            foreach (var responseHeader in responseHeaders)
            {
                headers.Add(responseHeader.Key, responseHeader.Value.First());
            }

            foreach (var contentHeader in contentHeaders)
            {
                headers.Add(contentHeader.Key, contentHeader.Value.First());
            }

            return headers;
        }

        public void AddInteraction(PactInteraction interaction)
        {
            _interactions = _interactions ?? new List<PactInteraction>();

            if (interaction == null)
            {
                return;
            }

            _interactions.Add(interaction);
        }

        public void AddInteractions(IEnumerable<PactInteraction> interactions)
        {
            //TODO: Maybe order in a predictable way, so that the file doesn't always change in git?
            if (interactions == null || !interactions.Any())
            {
                return;
            }

            foreach (var interaction in interactions)
            {
                AddInteraction(interaction);
            }
        }
    }
}