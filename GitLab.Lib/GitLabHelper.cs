namespace GitLab.Lib;

using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Helper.Lib;

public class GitLabHelper
{
    public static async Task<List<Repository>> GetRepositoriesAsync(IHttpClientFactory httpClientFactory, string projectUrl, string apiUrl, string filter, bool excludeFilter, string token)
    {
        List<Repository> repositories = new ();
        string json = await HttpHelper.InvokeRestCallAsync(httpClientFactory.CreateClient(), projectUrl, apiUrl, token);

        Repository[] allrepositories = JsonSerializer.Deserialize<Repository[]>(json);
        if (!string.IsNullOrEmpty(filter))
        {
            string[] repositoryFilters = filter.Split(",");
            foreach (var repo in allrepositories.OrderBy(r => r.name))
            {
                // if we are excluding repos
                if (excludeFilter)
                {
                    bool exclude = false;
                    foreach (string f in repositoryFilters)
                    {
                        Match m = Regex.Match(repo.name, f, RegexOptions.IgnoreCase);
                        if (m.Success)
                        {
                            // we have a match so exclude and break to save cycles
                            exclude = true;
                            break;
                        }
                    }

                    if (!exclude)
                    {
                        repositories.Add(repo);
                    }
                }
                else
                {
                    // we are including based on filter
                    foreach (string f in repositoryFilters)
                    {
                        Match m = Regex.Match(repo.name, filter, RegexOptions.IgnoreCase);
                        if (m.Success)
                        {
                            // we have a match so include and break to save cycles
                            //  ConsoleHelper.ConsoleWrite(programOptions.Verbose, $"Including {repo.name} per filter: {f}");
                            repositories.Add(repo);
                            break;
                        }
                    }
                }
            }
        }
        else
        {
            repositories.AddRange(allrepositories.OrderBy(r => r.name));
        }

       // repositories.Count = allrepositories.Count;
        return repositories;
    }
}