using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.EventHubs;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AzureEventHubToSplunkFunction
{
    public static class EventHubToSplunkfor
    {
        public static ILogger logger { get; set; }
        public static string splunkCertThumbprint = GetEnvironmentVariable("splunkCertThumbprint").ToLower();
        public static string splunkAddress = GetEnvironmentVariable("splunkAddress");
        public static string splunkToken = GetEnvironmentVariable("splunkToken");
        public static List<Exception> exceptions { get; set; }
        public static DateTime zeroTime = new DateTime(1970, 1, 1);
        public static HttpClient client = GetHttpClient();

        [FunctionName("SplunkEvents")]
        public static async Task Run(
            [EventHubTrigger("%eventHubName%", Connection = "eventHubConnectionString", ConsumerGroup = "%eventHubConsumerGroup%")]
            EventData[] events, ILogger log
        )
        {
            logger = log;
            exceptions = new List<Exception>();

            ValidateSplunkConfig();

            var requests = events
                .Select(e => ReadEventContent(e))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => (JSON: s, EventHubMessage: JsonConvert.DeserializeObject<EventHubMessage>(s)))
                .Where(t => ValidateEventHubMessage(t.JSON, t.EventHubMessage))
                .Select(t => EventToSplunkMessage(t.JSON, t.EventHubMessage))
                .Select(s => JsonConvert.SerializeObject(s))
                .Select(s => SendToSplunk(s));

            await Task.WhenAll(requests);

            ThrowExceptions();
            LogSuccess(events.Length);
        }

        private static void ValidateSplunkConfig()
        {
            if (string.IsNullOrWhiteSpace(splunkAddress))
                throw new Exception("Environment variable splunkAddress is required.");
            if (string.IsNullOrWhiteSpace(splunkToken))
                throw new Exception("Environment variable splunkToken is required.");
            if (string.IsNullOrWhiteSpace(splunkCertThumbprint))
                throw new Exception("Environment variable splunkCertThumbprint is required.");
        }

        private static string ReadEventContent(EventData eventData) =>
            Encoding.UTF8.GetString(eventData.Body.Array, eventData.Body.Offset, eventData.Body.Count);

        private static bool ValidateEventHubMessage(string json, EventHubMessage eventHubMessage)
        {
            if ((string.IsNullOrWhiteSpace(eventHubMessage.App) && string.IsNullOrWhiteSpace(eventHubMessage.Source)) || string.IsNullOrWhiteSpace(eventHubMessage.Time))
            {
                exceptions.Add(new Exception($"Skipping Event Hub event missing app/source and/or time properties: {json}"));
                return false;
            }
            return true;
        }

        private static SplunkMessage EventToSplunkMessage(string eventData, EventHubMessage eventHubMessage) =>
            new SplunkMessage()
            {
                SourceType = "_json",
                Source = eventHubMessage.GetSource(),
                Time = (DateTime.Parse(eventHubMessage.Time).Subtract(zeroTime)).TotalMilliseconds / 1000,
                Event = new JRaw(eventData)
            };

        private static async Task SendToSplunk(string splunkMessage)
        {
            try
            {
                logger.LogInformation($"Sending log to Splunk:\n{splunkMessage}");
                HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, splunkAddress);
                req.Headers.Accept.Clear();
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                req.Headers.Add("Authorization", $"Splunk {splunkToken}");
                req.Content = new StringContent(splunkMessage, Encoding.UTF8, "application/json");
                HttpResponseMessage response = await client.SendAsync(req);
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var res = JsonConvert.DeserializeObject<SplunkResponse>(content);
                    throw new HttpRequestException($"Splunk response: [{(int)response.StatusCode}/{response.StatusCode}] ({res.Code}) - {res.Text}");
                }
            }
            catch (Exception e)
            {
                exceptions.Add(e);
            }
        }

        private static void ThrowExceptions()
        {
            if (exceptions.Count > 1)
                throw new AggregateException(exceptions);
            if (exceptions.Count == 1)
                throw exceptions.Single();
        }

        private static void LogSuccess(int events) =>
            logger.LogInformation((events > 1) ? $"Successfully sent {events} logs to Splunk!" : $"Successfully sent log to Splunk!");

        private static string GetEnvironmentVariable(string name) =>
            System.Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process) ?? "";

        private static HttpClient GetHttpClient()
        {
            var handler = new HttpClientHandler();
            handler.ClientCertificateOptions = ClientCertificateOption.Manual;
            handler.SslProtocols = SslProtocols.Tls12;
            handler.ServerCertificateCustomValidationCallback = ValidateCertThumbprint;
            return new HttpClient(handler);
        }

        private static bool ValidateCertThumbprint(object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors sslErr)
        {
            var thumbprint = cert.GetCertHashString().ToLower();
            if (thumbprint == splunkCertThumbprint)
                return true;

            logger.LogError($"Unexpected Splunk certificate thumbprint. Expected thumbprint: {splunkCertThumbprint}, actual thumbprint: {thumbprint}.");
            return false;
        }
    }

    class EventHubMessage
    {
        [JsonProperty("time")]
        public string Time { get; set; }
        [JsonProperty("app")]
        public string App { get; set; }
        [JsonProperty("source")]
        public string Source { get; set; }

        public string GetSource() => string.IsNullOrWhiteSpace(Source) ? (App ?? "") : Source;
    }

    class SplunkMessage
    {
        [JsonProperty("event")]
        public JRaw Event { get; set; }
        [JsonProperty("time")]
        public double Time { get; set; }
        [JsonProperty("source")]
        public string Source { get; set; }
        [JsonProperty("sourcetype")]
        public string SourceType { get; set; }
    }

    public class SplunkResponse
    {
        [JsonProperty("text")]
        public string Text { get; set; }
        [JsonProperty("code")]
        public int Code { get; set; }
    }
}
