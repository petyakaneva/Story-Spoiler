using NUnit.Framework;
using RestSharp;
using RestSharp.Authenticators;
using StorySpoiler.Models;
using System.Net;
using System.Text.Json;


namespace StorySpoiler
{
    [TestFixture]
    public class StorySpoilerTests
    {
        private RestClient client;
        private static string lastCreatedStoryId;

        private const string BaseUrl = "https://d3s5nxhwblsjbi.cloudfront.net/api";
        private const string LoginUserName = "petya1";
        private const string LoginPassword = "petyapetya1";

        [OneTimeSetUp]
        public void Setup()
        {
            string jwtToken = GetJwtToken(LoginUserName, LoginPassword);

            var options = new RestClientOptions(BaseUrl)
            {
                Authenticator = new JwtAuthenticator(jwtToken),
            };

            client = new RestClient(options);
        }

        private static string GetJwtToken(string userName, string password)
        {
            var tempClient = new RestClient(BaseUrl);
            var request = new RestRequest("/User/Authentication", Method.Post)
                .AddJsonBody(new { userName, password });

            var response = tempClient.Execute(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), "Login should return 200 OK.");

            if (string.IsNullOrWhiteSpace(response.Content))
                throw new InvalidOperationException("Empty login response.");

            using var doc = JsonDocument.Parse(response.Content);
            if (doc.RootElement.TryGetProperty("accessToken", out var tokenProp))
            {
                var token = tokenProp.GetString();
                if (!string.IsNullOrWhiteSpace(token)) return token!;
            }

            throw new InvalidOperationException($"Failed to retrieve JWT token. Content: {response.Content}");
        }

        [Order(1)]
        [Test]
        public void CreateStory_WithRequiredFields_ShouldReturn201AndStoryIdAndMessage()
        {
            var body = new StoryDTO
            {
                Title = $"Test Story {DateTime.UtcNow.Ticks}",
                Description = "This is a test story description.",
                Url = ""
            };

            var request = new RestRequest("/Story/Create", Method.Post).AddJsonBody(body);
            var response = this.client.Execute(request);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), "Create should return 201 Created.");

            var parsed = JsonSerializer.Deserialize<ApiResponseDTO>(response.Content!, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            Assert.That(parsed?.StoryId, Is.Not.Null.And.Not.Empty, "StoryId should be returned.");
            StringAssert.Contains("Successfully created", parsed?.Msg ?? string.Empty, "Message should indicate successful creation.");

            lastCreatedStoryId = parsed!.StoryId!;
        }

        [Order(2)]
        [Test]
        public void EditExistingStory_ShouldReturn200AndSuccessMessage()
        {
            Assert.False(string.IsNullOrWhiteSpace(lastCreatedStoryId), "Missing StoryId from create.");

            var editBody = new StoryDTO
            {
                Title = "Edited Story",
                Description = "Updated test story description.",
                Url = ""
            };

            var request = new RestRequest($"/Story/Edit/{lastCreatedStoryId}", Method.Put)
                .AddJsonBody(editBody);

            var response = this.client.Execute(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), "Edit should return 200 OK.");

            var parsed = JsonSerializer.Deserialize<ApiResponseDTO>(response.Content!, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            StringAssert.Contains("Successfully edited", parsed?.Msg ?? string.Empty, "Message should indicate successful edit.");
        }

        [Order(3)]
        [Test]
        public void GetAllStories_ShouldReturn200AndNonEmptyArray()
        {
            var request = new RestRequest("/Story/All", Method.Get);
            var response = this.client.Execute(request);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), "Get All should return 200 OK.");
            Assert.IsNotNull(response.Content, "Response content should not be null.");

            using var doc = JsonDocument.Parse(response.Content!);
            Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Array), "Response should be a JSON array.");
            Assert.That(doc.RootElement.GetArrayLength(), Is.GreaterThan(0), "Stories list should not be empty.");
        }

        [Order(4)]
        [Test]
        public void DeleteStory_ShouldReturn200AndSuccessMessage()
        {
            Assert.False(string.IsNullOrWhiteSpace(lastCreatedStoryId), "Missing StoryId from create.");

            var request = new RestRequest($"/Story/Delete/{lastCreatedStoryId}", Method.Delete);
            var response = this.client.Execute(request);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), "Delete should return 200 OK.");

            var parsed = JsonSerializer.Deserialize<ApiResponseDTO>(response.Content!, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            StringAssert.Contains("Deleted successfully", parsed?.Msg ?? string.Empty, "Message should indicate successful deletion.");
        }

        [Order(5)]
        [Test]
        public void CreateStory_WithoutRequiredFields_ShouldReturn400()
        {
            var body = new { url = "" }; 

            var request = new RestRequest("/Story/Create", Method.Post).AddJsonBody(body);
            var response = this.client.Execute(request);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest),
                "Create without required fields should return 400 BadRequest.");
        }


        
        [Order(6)]
        [Test]
        public void EditNonExistingStory_ShouldReturn404AndNoSpoilersMessage()
        {
            var nonExistingId = string.IsNullOrWhiteSpace(lastCreatedStoryId)
         ? Guid.NewGuid().ToString()
         : lastCreatedStoryId;

            
            var preDelete = new RestRequest($"/Story/Delete/{nonExistingId}", Method.Delete);
            this.client.Execute(preDelete);

        
            var editRequest = new StoryDTO
            {
                Title = "AB",
                Description = "Valid description",
                Url = ""
            };

            var request = new RestRequest($"/Story/Edit/{nonExistingId}", Method.Put)
                .AddJsonBody(editRequest);

            var response = this.client.Execute(request);
            TestContext.Progress.WriteLine($"[EDIT non-existing] Status: {response.StatusCode} Body: {response.Content}");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound),
                "Edit non-existing should return 404 NotFound.");

            
            string msg = "";
            try
            {
                var parsed = JsonSerializer.Deserialize<ApiResponseDTO>(response.Content!,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                msg = parsed?.Msg ?? "";
            }
            catch { }

            if (string.IsNullOrWhiteSpace(msg)) msg = response.Content ?? "";

            StringAssert.Contains("No spoilers", msg, $"Expected 'No spoilers' in message. Actual: {msg}");
        }


        [Order(7)]
        [Test]
        public void DeleteNonExistingStory_ShouldReturn400AndUnableToDeleteMessage()
        {
            var nonExistingId = Guid.NewGuid().ToString();

            var request = new RestRequest($"/Story/Delete/{nonExistingId}", Method.Delete);
            var response = this.client.Execute(request);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest),
                "Delete non-existing should return 400 BadRequest.");

            var parsed = JsonSerializer.Deserialize<ApiResponseDTO>(response.Content!, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            StringAssert.Contains("Unable to delete this story spoiler", parsed?.Msg ?? string.Empty,
                "Message should indicate 'Unable to delete this story spoiler!'.");
        }


        [OneTimeTearDown]
        public void Teardown()
        {
            this.client?.Dispose();
        }
    }
}