using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Net;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using AppLensV3.Helpers;
using AppLensV3.Models;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using Microsoft.Extensions.Configuration;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Http;

namespace AppLensV3.Services
{
    public interface IIncidentAssistanceService
    {
        Task<bool> IsEnabled();
        Task<HttpResponseMessage> GetIncidentInfo(string incidentId);
        Task<HttpResponseMessage> ValidateAndUpdateIncident(string incidentId, object payload, string update);
        Task<HttpResponseMessage> GetOnboardedTeams(string userId);
        Task<HttpResponseMessage> GetTeamTemplate(string teamId, string incidentType, string userId);
        Task<HttpResponseMessage> UpdateTeamTemplate(string teamId, string incidentType, object payload, string userId);
        Task<HttpResponseMessage> TestTemplateWithIncident(object payload, string userId);
    }

    public class IncidentAssistanceService : IIncidentAssistanceService
    {
        private bool isEnabled;
        private string IncidentAssistEndpoint;
        private string ApiKey;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly Lazy<HttpClient> _client = new Lazy<HttpClient>(() =>
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return client;
        });

        private HttpClient _httpClient
        {
            get
            {
                return _client.Value;
            }
        }

        private HttpRequestMessage AddFunctionAuthHeaders(HttpRequestMessage request)
        {
            request.Headers.Add("x-functions-key", ApiKey);
            return request;
        }

        public IncidentAssistanceService(IConfiguration configuration, IHttpContextAccessor httpContextAccessor)
        {
            if (!bool.TryParse(configuration["IncidentAssistance:IsEnabled"].ToString(), out isEnabled))
            {
                isEnabled = false;
            }
            if (isEnabled)
            {
                IncidentAssistEndpoint = configuration["IncidentAssistance:IncidentAssistEndpoint"].ToString();
                ApiKey = configuration["IncidentAssistance:ApiKey"].ToString();
            }
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<bool> IsEnabled()
        {
            return isEnabled;
        }

        private Tuple<bool, string> CheckUserAccess(string allowedUsers)
        {
            if (string.IsNullOrWhiteSpace(allowedUsers))
            {
                return new Tuple<bool, string>(false, "Team admin has not set an allowed users list.");
            }
            var allowedUsersList = allowedUsers.ToLower().Split(',');
            string authHeader = _httpContextAccessor.HttpContext.Request.Headers.TryGetValue("Authorization", out var headerValue) ? headerValue.ToString() : null;
            if (!string.IsNullOrWhiteSpace(authHeader))
            {
                string accessToken = authHeader.Split(" ")[1];
                var token = new JwtSecurityToken(accessToken);
                object upn;
                string userAlias = null;
                if (token.Payload.TryGetValue("upn", out upn))
                {
                    userAlias = upn != null ? upn.ToString().Split('@')[0].ToLower() : null;
                }
                bool hasAccess = (!string.IsNullOrWhiteSpace(userAlias) && allowedUsersList.FirstOrDefault(x => x == userAlias) != null);
                return new Tuple<bool, string>(hasAccess, hasAccess ? "" : "User is not authorized to access this team template.");
            }
            else
            {
                return new Tuple<bool, string>(false, "Request does not have an authorization header.");
            }
        }

        public async Task<HttpResponseMessage> GetIncidentInfo(string incidentId)
        {
            if (string.IsNullOrWhiteSpace(incidentId))
            {
                throw new ArgumentException("incidentId");
            }
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, $"{IncidentAssistEndpoint}/api/GetIncidentInfo/{incidentId}");
            request = AddFunctionAuthHeaders(request);
            return await _httpClient.SendAsync(request);
        }

        public async Task<HttpResponseMessage> ValidateAndUpdateIncident(string incidentId, object payload, string update)
        {
            if (string.IsNullOrWhiteSpace(incidentId))
            {
                throw new ArgumentException("incidentId");
            }

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, $"{IncidentAssistEndpoint}/api/ValidateAndUpdateICM?update={update}");
            request = AddFunctionAuthHeaders(request);
            request.Content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            return await _httpClient.SendAsync(request);
        }

        public async Task<HttpResponseMessage> GetOnboardedTeams(string userId)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, $"{IncidentAssistEndpoint}/api/GetOnboardedTeams?userId={userId}");
            request = AddFunctionAuthHeaders(request);
            return await _httpClient.SendAsync(request);
        }

        public async Task<HttpResponseMessage> GetTeamTemplate(string teamId, string incidentType, string userId)
        {
            if (string.IsNullOrWhiteSpace(teamId))
            {
                throw new ArgumentException("teamId");
            }
            if (string.IsNullOrWhiteSpace(incidentType))
            {
                throw new ArgumentException("incidentType");
            }
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, $"{IncidentAssistEndpoint}/api/GetTeamTemplate/{teamId}/{incidentType}?userId={userId}");
            request = AddFunctionAuthHeaders(request);
            return await _httpClient.SendAsync(request);
        }

        public async Task<HttpResponseMessage> UpdateTeamTemplate(string teamId, string incidentType, object payload, string userId)
        {
            if (string.IsNullOrWhiteSpace(teamId))
            {
                throw new ArgumentException("teamId");
            }
            if (string.IsNullOrWhiteSpace(incidentType))
            {
                throw new ArgumentException("incidentType");
            }
            var getTemplateResponse = await GetTeamTemplate(teamId, incidentType, userId);
            if (getTemplateResponse.StatusCode == HttpStatusCode.Unauthorized)
            {
                return getTemplateResponse;
            }
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, $"{IncidentAssistEndpoint}/api/UpdateTeamTemplate/{teamId}/{incidentType}?userId={userId}");
            request = AddFunctionAuthHeaders(request);
            request.Content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            return await _httpClient.SendAsync(request);
        }

        public async Task<HttpResponseMessage> TestTemplateWithIncident(object payload, string userId) {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, $"{IncidentAssistEndpoint}/api/TestTemplateWithIncident?userId={userId}");
            request = AddFunctionAuthHeaders(request);
            request.Content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            return await _httpClient.SendAsync(request);
        }
    }
}