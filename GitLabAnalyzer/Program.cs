namespace GitLabAnalyzer;

using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using GitLab.Lib;
using CommandLine;
using Helper.Lib;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;

public class Program
{
    private static Options programOptions = new ();

    public static async Task Main(string[] args)
    {
        ServiceCollection services = new ();
        services.AddHttpClient();
        ServiceProvider provider = services.BuildServiceProvider();
        IHttpClientFactory httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();

        CultureInfo currentCulture = CultureInfo.CurrentCulture;
        StringBuilder sb = new ();
        DateTime start = DateTime.Now;
        string currentDirectory = Directory.GetCurrentDirectory();
        ConsoleHelper.WriteHeader("    GitLabAnalyzer");
        var result = Parser.Default.ParseArguments<Options>(args)
                .WithParsed(o =>
                {
                    programOptions = o;
                })
                .WithNotParsed(HandleParseError);

        if (result.Tag == ParserResultType.NotParsed)
        {
            // Help text requested, or parsing failed.
            return;
        }

        ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"Analyzing {programOptions.GitLabtUrl}, retrieving Repositories");

        List<Repository> repos = await GetAllRepositoriesAsync(httpClientFactory, programOptions.GitLabtUrl, programOptions.Token);
        if (!string.IsNullOrEmpty(programOptions.FromRepoActiveDate))
        {
            DateTime filterDate = DateTime.Parse(programOptions.FromRepoActiveDate);
            repos = repos.Where(r => r.last_activity_at >= filterDate).ToList();
        }

        sb.Clear();
        sb.AppendLine("id,name,name_with_namespace,path,path_with_namespace,created_at,updated_at,default_branch,ssh_url_to_repo,http_url_to_repo,web_url,last_activity_at,empty_repo,archived,visibility,storage_size,repository_size,wiki_size,job_artifacts_size,pipeline_artifacts_size,packages_size,snippets_size,uploads_size,container_registry_size,commit_count");

        int repoAddedCount = 0;
        foreach (Repository repo in repos)
        {
            if (repo.statistics != null)
            {
                sb.Append($"{repo.id},{repo.name},{repo.name_with_namespace},{repo.path},{repo.path_with_namespace},{repo.created_at},{repo.updated_at},{repo.default_branch},{repo.ssh_url_to_repo},{repo.http_url_to_repo},{repo.web_url},{repo.last_activity_at},{repo.empty_repo},{repo.archived},{repo.visibility},{repo.statistics.storage_size},{repo.statistics.repository_size},{repo.statistics.wiki_size},{repo.statistics.job_artifacts_size},{repo.statistics.pipeline_artifacts_size},{repo.statistics.packages_size},{repo.statistics.snippets_size},{repo.statistics.uploads_size},{repo.statistics.container_registry_size},{repo.statistics.commit_count}");
                sb.AppendLine();
                repoAddedCount++;
            }
        }

        programOptions.OutputFile = Path.Combine($"{currentDirectory}", $"repositories.csv");
        ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"Writing {repoAddedCount} of {repos.Count} Repositories to {programOptions.OutputFile}", ConsoleColor.Green);
        File.WriteAllText(programOptions.OutputFile, sb.ToString());
        sb.Clear();

        // Get all the commits
        sb.AppendLine("id,reponame,name_with_namespace, path, path_with_namespace, short_id,created_at,title,message,author_name,author_email,authored_date,committer_name,committer_email,commited_date,web.url,stats_additions,stats_deletions,stats_total");
        int i = 1;

        foreach (Repository repo in repos.Where(pp => pp.statistics.commit_count < 75000))
        {
            if (repo.statistics.commit_count > 0)
            {
                ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"Getting repository {i} of {repos.Count}", ConsoleColor.Green);

                List<Commit> commits = await GetAllCommitsAsync(httpClientFactory, programOptions.GitLabtUrl, programOptions.Token, repo);

                foreach (Commit c in commits)
                {
                    sb.AppendLine($"{c.id},{repo.name},{repo.name_with_namespace},{repo.path},{repo.path_with_namespace},{c.short_id},{c.created_at},{StringHelper.StringToCSVCell(c.title)},{StringHelper.StringToCSVCell(c.message)},{StringHelper.StringToCSVCell(c.author_name)},{c.author_email},{c.authored_date},{StringHelper.StringToCSVCell(c.committer_name)},{c.committer_email},{c.committed_date},{c.web_url},{c.stats.additions},{c.stats.deletions},{c.stats.total}");
                }

                programOptions.OutputFile = Path.Combine($"{currentDirectory}", $"commits.csv");
                ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"Writing {commits.Count} commits to {programOptions.OutputFile}", ConsoleColor.Green);
                if (i == 1)
                {
                    File.WriteAllText(programOptions.OutputFile, sb.ToString());
                }
                else
                {
                    File.AppendAllText(programOptions.OutputFile, sb.ToString());
                }

                sb.Clear();
            }

            i++;
        }

        TimeSpan t = DateTime.Now - start;
        ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"Analysis Completed in {t.Minutes}m: {t.Seconds}s");
    }

    public static async Task<List<Commit>> GetAllCommitsAsync(IHttpClientFactory httpClientFactory, string baseUrl, string token, Repository repo)
    {
        List<Commit> allCommits = new ();
        int page = 1;
        int perPage = 100; // Maximum allowed by GitLab API
        bool morePages = true;

        JsonSerializerOptions options = new ()
        {
            PropertyNameCaseInsensitive = true,
        };

        while (morePages)
        {
            ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"Getting {repo.name}, page: {page}", ConsoleColor.Green);

            string url = $"{baseUrl}/api/v4/projects/{repo.id}/repository/commits?with_stats=true&all=true&sort=asc&per_page={perPage}&page={page}";
            using (HttpClient client = httpClientFactory.CreateClient())
            {
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                try
                {
                    HttpResponseMessage response = await client.GetAsync(url);
                    response.EnsureSuccessStatusCode();
                    string responseBody = await response.Content.ReadAsStringAsync();

                    Commit[] commits = JsonSerializer.Deserialize<Commit[]>(responseBody, options);

                    if (commits != null && commits.Length > 0)
                    {
                        allCommits.AddRange(commits);
                        page++;
                    }
                    else
                    {
                        morePages = false;
                    }
                }
                catch (Exception ex)
                {
                    ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"Unexpected error: {ex.Message}", ConsoleColor.Red);
                    morePages = false;
                }
            }
        }

        return allCommits;
    }

    public static async Task<List<Repository>> GetAllRepositoriesAsync(IHttpClientFactory httpClientFactory, string baseUrl, string token)
    {
        List<Repository> allRepositories = new ();
        int page = 1;
        int perPage = 100; // Maximum allowed by GitLab API because... GitLab!
        bool morePages = true;

        JsonSerializerOptions options = new ()
        {
            PropertyNameCaseInsensitive = true,
        };

        while (morePages)
        {
            ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"GetAllRepositoriesAsync > Getting repositories, Page: {page}", ConsoleColor.Green);

            string url = $"{baseUrl}/api/v4/projects?order_by=name&statistics=true&sort=asc&per_page={perPage}&page={page}";
            using (HttpClient client = httpClientFactory.CreateClient())
            {
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                HttpResponseMessage response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();

                string responseBody = await response.Content.ReadAsStringAsync();

                Repository[] repositories = JsonSerializer.Deserialize<Repository[]>(responseBody, options);

                if (repositories != null && repositories.Length > 0)
                {
                    allRepositories.AddRange(repositories);
                    page++;
                }
                else
                {
                    morePages = false;
                }
            }
        }

        return allRepositories;
    }

    private static void WriteToFile(StringBuilder sb, bool firstProject)
    {
        if (firstProject)
        {
            File.WriteAllText(programOptions.OutputFile, sb.ToString());
        }
        else
        {
            File.AppendAllText(programOptions.OutputFile, sb.ToString());
        }

        sb.Clear();
    }

    private static void HandleParseError(IEnumerable<Error> errs)
    {
        foreach (var error in errs)
        {
            ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"Error = {error.Tag}");
        }
    }
}