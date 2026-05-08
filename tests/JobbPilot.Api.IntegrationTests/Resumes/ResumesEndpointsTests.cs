using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using JobbPilot.Api.IntegrationTests.Helpers;
using JobbPilot.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace JobbPilot.Api.IntegrationTests.Resumes;

[Collection("Api")]
public class ResumesEndpointsTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            _client,
            email: $"e2e-{Guid.NewGuid():N}@jobbpilot.test",
            ct: ct);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
    }

    private static HttpClient NewClientFor(ApiFactory f) => f.CreateClient();

    private static object CreateBody(string name = "Mitt CV", string fullName = "Klas Olsson") =>
        new { name, fullName };

    private static object MasterContentBody(
        string fullName = "Klas Olsson",
        string? summary = null) =>
        new
        {
            personalInfo = new
            {
                fullName,
                email = "klas@example.se",
                phone = (string?)null,
                location = "Stockholm"
            },
            experiences = Array.Empty<object>(),
            educations = Array.Empty<object>(),
            skills = Array.Empty<object>(),
            summary
        };

    // ---- Auth-skydd ---------------------------------------------------

    [Fact]
    public async Task GET_resumes_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.GetAsync("/api/v1/resumes", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_resume_by_id_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.GetAsync($"/api/v1/resumes/{Guid.NewGuid()}", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_resume_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.PostAsJsonAsync("/api/v1/resumes", CreateBody(), ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PATCH_resume_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.PatchAsJsonAsync(
            $"/api/v1/resumes/{Guid.NewGuid()}",
            new { name = "Nytt namn" },
            ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PUT_master_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.PutAsJsonAsync(
            $"/api/v1/resumes/{Guid.NewGuid()}/master",
            MasterContentBody(),
            ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DELETE_resume_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.DeleteAsync($"/api/v1/resumes/{Guid.NewGuid()}", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DELETE_resume_version_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.DeleteAsync(
            $"/api/v1/resumes/{Guid.NewGuid()}/versions/{Guid.NewGuid()}", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---- Lyckade flöden -----------------------------------------------

    [Fact]
    public async Task POST_resume_returns_201_with_id()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.PostAsJsonAsync("/api/v1/resumes", CreateBody(), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var id = json.GetProperty("id").GetString();
        id.ShouldNotBeNullOrEmpty();
        Guid.Parse(id!).ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task GET_resumes_with_auth_returns_200_with_array()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.GetAsync("/api/v1/resumes", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.ValueKind.ShouldBe(JsonValueKind.Array);
    }

    [Fact]
    public async Task POST_then_GET_list_includes_created_resume()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var post = await _client.PostAsJsonAsync("/api/v1/resumes", CreateBody("CV-A"), ct);
        post.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await post.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;

        var list = await _client.GetAsync("/api/v1/resumes", ct);
        list.StatusCode.ShouldBe(HttpStatusCode.OK);
        var listJson = await list.Content.ReadFromJsonAsync<JsonElement>(ct);

        var found = listJson.EnumerateArray().Any(r =>
            r.GetProperty("id").GetString() == id &&
            r.GetProperty("name").GetString() == "CV-A");
        found.ShouldBeTrue();
    }

    [Fact]
    public async Task GET_resume_by_id_returns_detail_with_master_version()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var post = await _client.PostAsJsonAsync("/api/v1/resumes", CreateBody(), ct);
        var id = (await post.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;

        var get = await _client.GetAsync($"/api/v1/resumes/{id}", ct);

        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await get.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("id").GetString().ShouldBe(id);
        json.GetProperty("name").GetString().ShouldBe("Mitt CV");
        var versions = json.GetProperty("versions");
        versions.ValueKind.ShouldBe(JsonValueKind.Array);
        versions.GetArrayLength().ShouldBe(1);
        versions[0].GetProperty("kind").GetString().ShouldBe("Master");
    }

    [Fact]
    public async Task GET_resume_by_unknown_id_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.GetAsync($"/api/v1/resumes/{Guid.NewGuid()}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PATCH_resume_renames_and_GET_reflects_new_name()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var post = await _client.PostAsJsonAsync("/api/v1/resumes", CreateBody("Innan"), ct);
        var id = (await post.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;

        var patch = await _client.PatchAsJsonAsync(
            $"/api/v1/resumes/{id}",
            new { name = "Efter" },
            ct);
        patch.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var get = await _client.GetAsync($"/api/v1/resumes/{id}", ct);
        var json = await get.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("name").GetString().ShouldBe("Efter");
    }

    [Fact]
    public async Task PUT_master_updates_content_and_GET_reflects_summary()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var post = await _client.PostAsJsonAsync("/api/v1/resumes", CreateBody(), ct);
        var id = (await post.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;

        var put = await _client.PutAsJsonAsync(
            $"/api/v1/resumes/{id}/master",
            MasterContentBody(summary: "Backendutvecklare med fokus på .NET."),
            ct);
        put.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var get = await _client.GetAsync($"/api/v1/resumes/{id}", ct);
        var json = await get.Content.ReadFromJsonAsync<JsonElement>(ct);
        var versions = json.GetProperty("versions");
        var master = versions.EnumerateArray()
            .First(v => v.GetProperty("kind").GetString() == "Master");
        master.GetProperty("content").GetProperty("summary").GetString()
            .ShouldBe("Backendutvecklare med fokus på .NET.");
    }

    [Fact]
    public async Task PUT_master_round_trips_full_resume_content_through_jsonb()
    {
        // Regression-skydd: ResumeContent serialiseras/deserialiseras via System.Text.Json
        // mot JSONB-kolumnen (ResumeVersionConfiguration). Detta test säkerställer att
        // alla nästade collections (Experiences, Educations, Skills) round-tripar
        // korrekt — om STJ ctor-matching brister upptäcks det här.
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var post = await _client.PostAsJsonAsync("/api/v1/resumes", CreateBody(), ct);
        var id = (await post.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;

        var fullContent = new
        {
            personalInfo = new
            {
                fullName = "Klas Olsson",
                email = "klas@example.se",
                phone = "+46701234567",
                location = "Stockholm"
            },
            experiences = new[]
            {
                new
                {
                    company = "Acme AB",
                    role = "Senior Backend",
                    startDate = "2022-01-01",
                    endDate = (string?)"2024-06-30",
                    description = (string?)"Byggde Clean Arch-lösning i .NET."
                },
                new
                {
                    company = "Beta AB",
                    role = "Lead Engineer",
                    startDate = "2024-07-01",
                    endDate = (string?)null,
                    description = (string?)null
                }
            },
            educations = new[]
            {
                new
                {
                    institution = "NBI/Handelsakademin",
                    degree = ".NET-utvecklare",
                    startDate = "2025-08-15",
                    endDate = "2027-06-15"
                }
            },
            skills = new[]
            {
                new { name = "C#", yearsExperience = (int?)10 },
                new { name = "PostgreSQL", yearsExperience = (int?)null }
            },
            summary = "Sammanfattning av profil."
        };

        var put = await _client.PutAsJsonAsync($"/api/v1/resumes/{id}/master", fullContent, ct);
        put.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var get = await _client.GetAsync($"/api/v1/resumes/{id}", ct);
        var json = await get.Content.ReadFromJsonAsync<JsonElement>(ct);
        var content = json.GetProperty("versions").EnumerateArray()
            .First(v => v.GetProperty("kind").GetString() == "Master")
            .GetProperty("content");

        content.GetProperty("personalInfo").GetProperty("fullName").GetString().ShouldBe("Klas Olsson");
        content.GetProperty("personalInfo").GetProperty("phone").GetString().ShouldBe("+46701234567");

        var experiences = content.GetProperty("experiences").EnumerateArray().ToList();
        experiences.Count.ShouldBe(2);
        experiences[0].GetProperty("company").GetString().ShouldBe("Acme AB");
        experiences[0].GetProperty("role").GetString().ShouldBe("Senior Backend");
        experiences[0].GetProperty("startDate").GetString().ShouldBe("2022-01-01");
        experiences[0].GetProperty("endDate").GetString().ShouldBe("2024-06-30");
        experiences[1].GetProperty("endDate").ValueKind.ShouldBe(JsonValueKind.Null);
        experiences[1].GetProperty("description").ValueKind.ShouldBe(JsonValueKind.Null);

        var educations = content.GetProperty("educations").EnumerateArray().ToList();
        educations.Count.ShouldBe(1);
        educations[0].GetProperty("institution").GetString().ShouldBe("NBI/Handelsakademin");
        educations[0].GetProperty("degree").GetString().ShouldBe(".NET-utvecklare");

        var skills = content.GetProperty("skills").EnumerateArray().ToList();
        skills.Count.ShouldBe(2);
        skills[0].GetProperty("name").GetString().ShouldBe("C#");
        skills[0].GetProperty("yearsExperience").GetInt32().ShouldBe(10);
        skills[1].GetProperty("yearsExperience").ValueKind.ShouldBe(JsonValueKind.Null);

        content.GetProperty("summary").GetString().ShouldBe("Sammanfattning av profil.");
    }

    [Fact]
    public async Task DELETE_resume_returns_204_and_subsequent_GET_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var post = await _client.PostAsJsonAsync("/api/v1/resumes", CreateBody(), ct);
        var id = (await post.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;

        var del = await _client.DeleteAsync($"/api/v1/resumes/{id}", ct);
        del.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var get = await _client.GetAsync($"/api/v1/resumes/{id}", ct);
        get.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DELETE_master_version_returns_400_with_problem_details()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var post = await _client.PostAsJsonAsync("/api/v1/resumes", CreateBody(), ct);
        var id = (await post.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;

        var get = await _client.GetAsync($"/api/v1/resumes/{id}", ct);
        var detail = await get.Content.ReadFromJsonAsync<JsonElement>(ct);
        var masterVersionId = detail.GetProperty("versions")[0].GetProperty("id").GetString()!;

        var del = await _client.DeleteAsync($"/api/v1/resumes/{id}/versions/{masterVersionId}", ct);

        del.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ---- Validering ----------------------------------------------------

    [Fact]
    public async Task POST_resume_with_empty_name_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.PostAsJsonAsync(
            "/api/v1/resumes",
            new { name = "", fullName = "Klas Olsson" },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task POST_resume_with_empty_fullName_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.PostAsJsonAsync(
            "/api/v1/resumes",
            new { name = "Mitt CV", fullName = "" },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PATCH_resume_with_empty_name_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var post = await _client.PostAsJsonAsync("/api/v1/resumes", CreateBody(), ct);
        var id = (await post.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;

        var patch = await _client.PatchAsJsonAsync(
            $"/api/v1/resumes/{id}",
            new { name = "" },
            ct);

        patch.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ---- Cross-tenant-skydd -------------------------------------------

    [Fact]
    public async Task GET_resume_belonging_to_other_user_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;

        // User A skapar ett CV
        var clientA = NewClientFor(factory);
        var sessionA = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            clientA, email: $"e2e-a-{Guid.NewGuid():N}@jobbpilot.test", ct: ct);
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionA);

        var postA = await clientA.PostAsJsonAsync("/api/v1/resumes", CreateBody("CV för A"), ct);
        var idA = (await postA.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;

        // User B försöker hämta A:s CV
        var clientB = NewClientFor(factory);
        var sessionB = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            clientB, email: $"e2e-b-{Guid.NewGuid():N}@jobbpilot.test", ct: ct);
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionB);

        var getB = await clientB.GetAsync($"/api/v1/resumes/{idA}", ct);

        getB.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PATCH_resume_belonging_to_other_user_returns_problem_response()
    {
        var ct = TestContext.Current.CancellationToken;

        var clientA = NewClientFor(factory);
        var sessionA = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            clientA, email: $"e2e-a-{Guid.NewGuid():N}@jobbpilot.test", ct: ct);
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionA);

        var postA = await clientA.PostAsJsonAsync("/api/v1/resumes", CreateBody(), ct);
        var idA = (await postA.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;

        var clientB = NewClientFor(factory);
        var sessionB = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            clientB, email: $"e2e-b-{Guid.NewGuid():N}@jobbpilot.test", ct: ct);
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionB);

        var patchB = await clientB.PatchAsJsonAsync(
            $"/api/v1/resumes/{idA}",
            new { name = "Hijack" },
            ct);

        // Handler kastar NotFoundException vilket ASP.NET-pipelinen mappar till
        // 404 (eller Problem-response). Kontrakt: ej 2xx (cross-tenant skydd håller).
        ((int)patchB.StatusCode).ShouldBeGreaterThanOrEqualTo(400);
    }

    [Fact]
    public async Task DELETE_resume_belonging_to_other_user_returns_problem_response()
    {
        var ct = TestContext.Current.CancellationToken;

        var clientA = NewClientFor(factory);
        var sessionA = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            clientA, email: $"e2e-a-{Guid.NewGuid():N}@jobbpilot.test", ct: ct);
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionA);

        var postA = await clientA.PostAsJsonAsync("/api/v1/resumes", CreateBody(), ct);
        var idA = (await postA.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;

        var clientB = NewClientFor(factory);
        var sessionB = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            clientB, email: $"e2e-b-{Guid.NewGuid():N}@jobbpilot.test", ct: ct);
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionB);

        var delB = await clientB.DeleteAsync($"/api/v1/resumes/{idA}", ct);

        ((int)delB.StatusCode).ShouldBeGreaterThanOrEqualTo(400);

        // Verifiera att A:s CV finns kvar
        var getA = await clientA.GetAsync($"/api/v1/resumes/{idA}", ct);
        getA.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---- Komplett livscykel -------------------------------------------

    [Fact]
    public async Task FullLifecycle_create_list_get_rename_updateMaster_delete()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        // 1. Skapa
        var post = await _client.PostAsJsonAsync("/api/v1/resumes", CreateBody("Initial"), ct);
        post.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await post.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;

        // 2. Lista innehåller det
        var list = await _client.GetAsync("/api/v1/resumes", ct);
        list.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await list.Content.ReadFromJsonAsync<JsonElement>(ct))
            .EnumerateArray().Any(r => r.GetProperty("id").GetString() == id).ShouldBeTrue();

        // 3. Hämta detalj
        var detail = await _client.GetAsync($"/api/v1/resumes/{id}", ct);
        detail.StatusCode.ShouldBe(HttpStatusCode.OK);

        // 4. Rename
        var rename = await _client.PatchAsJsonAsync(
            $"/api/v1/resumes/{id}",
            new { name = "Slutgiltigt namn" },
            ct);
        rename.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // 5. Update master
        var put = await _client.PutAsJsonAsync(
            $"/api/v1/resumes/{id}/master",
            MasterContentBody(summary: "Sammanfattning."),
            ct);
        put.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // 6. Verifiera state
        var afterUpdate = await _client.GetAsync($"/api/v1/resumes/{id}", ct);
        var afterJson = await afterUpdate.Content.ReadFromJsonAsync<JsonElement>(ct);
        afterJson.GetProperty("name").GetString().ShouldBe("Slutgiltigt namn");

        // 7. Radera
        var del = await _client.DeleteAsync($"/api/v1/resumes/{id}", ct);
        del.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var gone = await _client.GetAsync($"/api/v1/resumes/{id}", ct);
        gone.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
