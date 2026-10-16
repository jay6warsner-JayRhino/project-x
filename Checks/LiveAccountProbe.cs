using AuraOne.Integrations;
public static class LiveAccountProbe
{
    sealed class Tokens(string token):IAccountAccessTokenProvider
    {
        public Task<string?> GetTokenAsync(AccountProvider provider,CancellationToken cancellation)
        {cancellation.ThrowIfCancellationRequested();return Task.FromResult<string?>(provider==AccountProvider.GitHub?token:null);}
    }
    public static async Task Run()
    {
        var token=Environment.GetEnvironmentVariable("AURA_CHECK_GITHUB_TOKEN");
        if(string.IsNullOrWhiteSpace(token))throw new InvalidOperationException("An authorized GitHub credential is required for this optional live probe.");
        using var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(25)};
        var api=new ReadOnlyAccountTools(client,new Tokens(token));
        using var repos=await api.GitHubRepositoriesAsync(20,CancellationToken.None);
        if(repos.RootElement.ValueKind!=System.Text.Json.JsonValueKind.Array)throw new InvalidOperationException("Unexpected repository response.");
        using var issues=await api.GitHubIssuesAsync("jay6warsner-JayRhino","project-x",20,CancellationToken.None);
        if(issues.RootElement.ValueKind!=System.Text.Json.JsonValueKind.Array)throw new InvalidOperationException("Unexpected issue response.");
        Console.WriteLine("PASS live GitHub account adapter: authenticated repositories and project-x issues returned JSON arrays.");
        Console.WriteLine("No credentials or private repository contents are printed. Native phone authorization remains a separate test.");
    }
}
