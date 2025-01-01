using LibGit2Sharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Text;
using System.Web;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.MapPost("/webhook", async context =>
{
    using var reader = new StreamReader(context.Request.Body);
    var payload = await reader.ReadToEndAsync();

    // Parse payload
    var decode = HttpUtility.UrlDecode(payload);
    var json = JObject.Parse(decode.Substring(8));
    var branch = json["ref"]?.ToString()?.Replace("refs/heads/", ""); // Lấy tên nhánh
    var repository = json["repository"]?["full_name"]?.ToString(); // Lấy tên repository

    if (!string.IsNullOrEmpty(branch) && !string.IsNullOrEmpty(repository))
    {
        Console.WriteLine($"Code pushed to branch: {branch} in repository: {repository}");

        Console.OutputEncoding = Encoding.UTF8;
        string repoUrl = "https://github.com/chivy140820a/demo-custom-ci-cd.git";  // URL của repository GitHub
        string localRepoPath = @"D:\MyProduct\Demo_custom_CICD";  // Đường dẫn nơi clone repo
        string iisAppPath = @"D:\MyProduct\publishs\myapp";  // Đường dẫn ứng dụng trong IIS
        string buildConfiguration = "Release";  // Configuration build (Release hoặc Debug)
        try
        {
            //Bước 1: Clone repository
            if (Directory.Exists(localRepoPath))
            {
                Console.WriteLine("Repository đã tồn tại, thực hiện pull.");
                PullRepository(localRepoPath, branch);
            }
            else
            {
                Console.WriteLine("Cloning repository...");
                CloneRepository(repoUrl, localRepoPath, branch);
            }

            // Bước 2: Build ứng dụng
            BuildApplication(localRepoPath, buildConfiguration);

            // Bước 3: Chạy Unit Tests
            RunTests(localRepoPath);

            // Bước 4: Đo độ phủ code
            RunCodeCoverage(localRepoPath);

            // Bước 5: Publish ứng dụng
            PublishApplication(localRepoPath);

            //Bước 6: Deploy ứng dụng lên IIS
            DeployToIIS(localRepoPath, iisAppPath);

            Console.WriteLine("CI/CD pipeline hoàn thành thành công!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Lỗi: {ex.Message}");
        }
    }

    await context.Response.WriteAsync("Webhook received!");
});
void CloneRepository(string repoUrl, string localPath, string branch)
{
    Console.WriteLine($"Cloning từ {repoUrl}...");
    Repository.Clone(repoUrl, localPath);
    using (var repository = new Repository(localPath))
    {
        Commands.Checkout(repository, branch);
    }
    Console.WriteLine("Clone hoàn tất.");
}
static void PullRepository(string localPath, string branch)
{
    using (var repo = new Repository(localPath))
    {
        if (repo.Head.FriendlyName != branch)
        {
            Console.WriteLine($"Checking out branch '{branch}'...");
            Commands.Checkout(repo, branch);
        }
        Console.WriteLine("Pulling latest changes...");
        var remote = repo.Network.Remotes["origin"];
        Commands.Pull(repo, new Signature("user", "email", DateTimeOffset.Now), new PullOptions());
    }
    Console.WriteLine("Pull hoàn tất.");
}
void BuildApplication(string projectPath, string configuration)
{
    RunProcess("dotnet", $"build {projectPath} --configuration {configuration}", projectPath);
}

void RunTests(string projectPath)
{
    RunProcess("dotnet", $"test {projectPath} --configuration Release --no-build", projectPath);
}

void RunCodeCoverage(string projectPath)
{
    RunProcess("dotnet", $"test {projectPath} --collect:\"Code Coverage\"", projectPath);
}

void PublishApplication(string projectPath)
{
    string publishPath = Path.Combine(projectPath, "publish");
    RunProcess("dotnet", $"publish {projectPath} --configuration Release --output {publishPath}", projectPath);
}

void DeployToIIS(string projectPath, string iisAppPath)
{
    Console.WriteLine("Deploying to IIS...");
    string publishPath = Path.Combine(projectPath, "publish");
    if (Directory.Exists(iisAppPath))
    {
        // Lấy danh sách tất cả các tệp trong thư mục
        string[] files = Directory.GetFiles(iisAppPath);

        foreach (string file in files)
        {
            // Xóa từng tệp
            File.Delete(file);
            Console.WriteLine($"Đã xóa tệp: {file}");
        }

        Console.WriteLine("Tất cả các tệp đã được xóa thành công.");
    }
    if (Directory.Exists(iisAppPath))
    {
        Directory.Delete(iisAppPath,true);
    }
    Directory.CreateDirectory(iisAppPath);

    CopyDirectory(publishPath, iisAppPath);
    Console.WriteLine("Deploy hoàn tất.");
}

void CopyDirectory(string sourceDir, string destDir)
{
    foreach (string dirPath in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
        Directory.CreateDirectory(dirPath.Replace(sourceDir, destDir));

    foreach (string newPath in Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories))
        File.Copy(newPath, newPath.Replace(sourceDir, destDir), true);
}

void RunProcess(string fileName, string arguments, string workingDirectory)
{
    Console.WriteLine($"Chạy lệnh: {fileName} {arguments}");
    var processStartInfo = new ProcessStartInfo
    {
        FileName = fileName,
        Arguments = arguments,
        WorkingDirectory = workingDirectory,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true,
        UseShellExecute = false
    };

    var process = new Process { StartInfo = processStartInfo };
    process.OutputDataReceived += (sender, e) => { if (e.Data != null) Console.WriteLine($"[OUTPUT] {e.Data}"); };
    process.ErrorDataReceived += (sender, e) => { if (e.Data != null) Console.WriteLine($"[ERROR] {e.Data}"); };

    process.Start();
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();
    process.WaitForExit();

    if (process.ExitCode != 0)
    {
        throw new Exception($"Lệnh {fileName} {arguments} thất bại với mã thoát {process.ExitCode}");
    }
    Console.WriteLine("Lệnh thực thi thành công.");
}

app.Run();
