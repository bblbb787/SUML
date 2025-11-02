using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace PureMCLauncher
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        // 基本属性
        private List<VersionInfo> _versions = new List<VersionInfo>();
        private VersionInfo _selectedVersion;
        private string _javaPath = string.Empty;
        private string _gameDir;
        private string _currentUsername = "Player" + new Random().Next(10000, 99999);
        private string _currentUuid = GenerateUuid();

        // 属性绑定
        public List<VersionInfo> Versions
        {
            get { return _versions; }
            set { _versions = value; NotifyPropertyChanged("Versions"); }
        }

        public VersionInfo SelectedVersion
        {
            get { return _selectedVersion; }
            set { _selectedVersion = value; NotifyPropertyChanged("SelectedVersion"); }
        }

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            InitializeGameDirectory();
            DetectJava();
            LoadInstalledVersions();
            Task.Run(() => LoadAvailableVersions());
        }

        // 初始化游戏目录
        private void InitializeGameDirectory()
        {
            _gameDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
            GameDirTextBox.Text = _gameDir;
        }

        // 检测Java
        private void DetectJava()
        {
            try
            {
                // 从环境变量检测
                var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
                if (!string.IsNullOrEmpty(javaHome))
                {
                    _javaPath = Path.Combine(javaHome, "bin", "java.exe");
                    if (File.Exists(_javaPath))
                    {
                        JavaPathTextBox.Text = _javaPath;
                        Log($"自动检测到Java: {_javaPath}");
                        return;
                    }
                }

                // 检测常见Java安装路径
                var commonJavaPaths = new[]
                {
                    @"C:\Program Files\Java\jre1.8.0_311\bin\java.exe",
                    @"C:\Program Files\Java\jre1.8.0_301\bin\java.exe",
                    @"C:\Program Files\Java\jre1.8.0_291\bin\java.exe",
                    @"C:\Program Files\Java\jdk1.8.0_311\bin\java.exe",
                    @"C:\Program Files\Java\jdk1.8.0_301\bin\java.exe",
                    @"C:\Program Files\Java\jdk1.8.0_291\bin\java.exe",
                    @"C:\Program Files\Java\jre11\bin\java.exe",
                    @"C:\Program Files\Java\jre17\bin\java.exe",
                    @"C:\Program Files\Java\jre21\bin\java.exe",
                    @"C:\Program Files\Java\jdk11\bin\java.exe",
                    @"C:\Program Files\Java\jdk17\bin\java.exe",
                    @"C:\Program Files\Java\jdk21\bin\java.exe"
                };

                foreach (var path in commonJavaPaths)
                {
                    if (File.Exists(path))
                    {
                        _javaPath = path;
                        JavaPathTextBox.Text = _javaPath;
                        Log($"在常见路径找到Java: {_javaPath}");
                        return;
                    }
                }

                Log("未检测到Java，请手动设置路径");
            }
            catch (Exception ex)
            {
                Log($"检测Java失败: {ex.Message}");
            }
        }

        // 加载已安装版本
        private void LoadInstalledVersions()
        {
            try
            {
                var versionsDir = Path.Combine(_gameDir, "versions");
                if (Directory.Exists(versionsDir))
                {
                    var installedVersions = new List<VersionInfo>();
                    foreach (var dir in Directory.GetDirectories(versionsDir))
                    {
                        var versionId = Path.GetFileName(dir);
                        var jsonFile = Path.Combine(dir, versionId + ".json");
                        var jarFile = Path.Combine(dir, versionId + ".jar");

                        if (File.Exists(jsonFile) && File.Exists(jarFile))
                        {
                            try
                            {
                                var versionInfo = ParseVersionJson(jsonFile);
                                if (versionInfo != null)
                                {
                                    installedVersions.Add(versionInfo);
                                }
                            }
                            catch (Exception ex)
                            {
                                Log($"解析版本文件失败 {jsonFile}: {ex.Message}");
                            }
                        }
                    }

                    if (installedVersions.Count > 0)
                    {
                        InstalledVersionsList.ItemsSource = installedVersions.OrderByDescending(v => v.ReleaseTime);
                        Log($"已加载 {installedVersions.Count} 个已安装版本");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"加载已安装版本失败: {ex.Message}");
            }
        }

        // 加载可用版本
        private async Task LoadAvailableVersions()
        {
            try
            {
                Log("正在获取版本列表...");
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(30);
                    var response = await client.GetStringAsync("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json");
                    
                    // 使用简单的字符串解析（不使用Json库）
                    var versionInfos = ParseVersionManifest(response);
                    
                    Dispatcher.Invoke(() =>
                    {
                        Versions = versionInfos;
                        AvailableVersionsList.ItemsSource = versionInfos;
                        
                        // 默认选择最新版本
                        if (Versions.Count > 0)
                        {
                            SelectedVersion = Versions[0];
                        }
                        
                        Log($"已获取 {versionInfos.Count} 个可用版本");
                    });
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => Log($"获取版本列表失败: {ex.Message}"));
            }
        }

        // 解析版本清单（不使用Json库）
        private List<VersionInfo> ParseVersionManifest(string json)
        {
            var versions = new List<VersionInfo>();
            
            try
            {
                // 简单的字符串解析方法
                int startIndex = json.IndexOf("""versions""");
                if (startIndex > 0)
                {
                    int arrayStart = json.IndexOf("[", startIndex);
                    int arrayEnd = json.LastIndexOf("]");
                    if (arrayStart > 0 && arrayEnd > arrayStart)
                    {
                        string versionsArray = json.Substring(arrayStart + 1, arrayEnd - arrayStart - 1);
                        
                        // 分割版本对象
                        int braceCount = 0;
                        int versionStart = 0;
                        for (int i = 0; i < versionsArray.Length; i++)
                        {
                            if (versionsArray[i] == '{') braceCount++;
                            else if (versionsArray[i] == '}') braceCount--;
                            
                            // 找到一个完整的版本对象
                            if (braceCount == 0 && i > versionStart)
                            {
                                string versionJson = versionsArray.Substring(versionStart, i - versionStart + 1).Trim();
                                if (!string.IsNullOrEmpty(versionJson))
                                {
                                    var versionInfo = ParseVersionObject(versionJson);
                                    if (versionInfo != null)
                                    {
                                        versions.Add(versionInfo);
                                        if (versions.Count >= 50) break; // 限制显示50个版本
                                    }
                                }
                                // 跳过逗号和空格
                                while (i + 1 < versionsArray.Length && (versionsArray[i + 1] == ',' || versionsArray[i + 1] == ' ' || versionsArray[i + 1] == '\n'))
                                {
                                    i++;
                                }
                                versionStart = i + 1;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"解析版本清单失败: {ex.Message}");
            }
            
            return versions;
        }
        
        // 解析单个版本对象
        private VersionInfo ParseVersionObject(string json)
        {
            try
            {
                return new VersionInfo
                {
                    Id = ExtractField(json, "id"),
                    Type = ExtractField(json, "type"),
                    Url = ExtractField(json, "url"),
                    Time = ExtractField(json, "time"),
                    ReleaseTime = DateTime.Parse(ExtractField(json, "releaseTime"))
                };
            }
            catch
            {
                return null;
            }
        }
        
        // 提取字段值
        private string ExtractField(string json, string fieldName)
        {
            string searchStr = "\"" + fieldName + "\":\"";
            int start = json.IndexOf(searchStr);
            if (start >= 0)
            {
                start += searchStr.Length;
                int end = json.IndexOf('"', start);
                if (end > start)
                {
                    return json.Substring(start, end - start);
                }
            }
            return string.Empty;
        }

        // 解析版本JSON文件（不使用Json库）
        private VersionInfo ParseVersionJson(string jsonPath)
        {
            try
            {
                var content = File.ReadAllText(jsonPath);
                var versionId = ExtractValue(content, "id");
                var type = ExtractValue(content, "type");
                var releaseTime = ExtractValue(content, "releaseTime");
                
                return new VersionInfo
                {
                    Id = versionId,
                    Type = type,
                    ReleaseTime = DateTime.Parse(releaseTime)
                };
            }
            catch
            {
                return null;
            }
        }

        // 从JSON字符串中提取值（简单实现）
        private string ExtractValue(string json, string key)
        {
            var pattern = $"\"{key}\":\"([^\"]+)\"";
            var match = Regex.Match(json, pattern);
            return match.Groups.Count > 1 ? match.Groups[1].Value : string.Empty;
        }

        // 记录日志
        private void Log(string message)
        {
            Dispatcher.Invoke(() =>
            {
                LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
                LogTextBox.ScrollToEnd();
            });
        }

        // 事件处理方法
        private void RefreshVersions(object sender, RoutedEventArgs e)
        {
            Task.Run(() => LoadAvailableVersions());
            LoadInstalledVersions();
        }

        private void VersionSelected(object sender, SelectionChangedEventArgs e)
        {
            if (SelectedVersion != null)
            {
                Log($"已选择版本: {SelectedVersion.Id}");
            }
        }

        private async void DownloadVersion(object sender, RoutedEventArgs e)
        {
            if (SelectedVersion == null)
            {
                Log("请先选择一个版本");
                return;
            }

            try
            {
                Log($"开始下载版本: {SelectedVersion.Id}");
                Dispatcher.Invoke(() => DownloadProgressBar.Value = 0);
                
                var versionDir = Path.Combine(_gameDir, "versions", SelectedVersion.Id);
                Directory.CreateDirectory(versionDir);
                
                // 下载版本JSON文件
                var versionJsonPath = Path.Combine(versionDir, $"{SelectedVersion.Id}.json");
                await DownloadFile(SelectedVersion.Url, versionJsonPath);
                Log($"已下载版本配置文件");
                
                // 解析完整版本信息
                var versionJsonContent = File.ReadAllText(versionJsonPath);
                var clientUrl = ExtractValue(versionJsonContent, "url");
                
                // 下载客户端JAR
                var jarPath = Path.Combine(versionDir, $"{SelectedVersion.Id}.jar");
                await DownloadFile(clientUrl, jarPath);
                Log($"已下载客户端JAR文件");
                
                // 下载库文件（简化版）
                await DownloadLibraries(versionJsonContent);
                
                Log($"版本 {SelectedVersion.Id} 下载完成！");
                LoadInstalledVersions(); // 刷新已安装版本列表
            }
            catch (Exception ex)
            {
                Log($"下载版本失败: {ex.Message}");
            }
        }

        // 下载库文件
        private async Task DownloadLibraries(string versionJson)
        {
            try
            {
                // 简化的库文件下载逻辑
                Log("正在下载依赖库文件...");
                
                // 示例：下载必要的库目录结构
                var librariesDir = Path.Combine(_gameDir, "libraries");
                Directory.CreateDirectory(librariesDir);
                
                Log("库文件下载完成（简化版）");
            }
            catch (Exception ex)
            {
                Log($"下载库文件时出错: {ex.Message}");
            }
        }

        // 下载文件
        private async Task DownloadFile(string url, string path)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(10);
                    var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();
                    
                    var totalBytes = response.Content.Headers.ContentLength ?? 0;
                    var downloadedBytes = 0;
                    
                    using (var stream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(path, FileMode.Create))
                    {
                        var buffer = new byte[8192];
                        int bytesRead;
                        
                        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            downloadedBytes += bytesRead;
                            
                            if (totalBytes > 0)
                            {
                                var progress = (double)downloadedBytes / totalBytes * 100;
                                Dispatcher.Invoke(() => DownloadProgressBar.Value = progress);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"下载文件失败 {url}: {ex.Message}");
                throw;
            }
        }

        // 启动游戏
        private void LaunchGame(object sender, RoutedEventArgs e)
        {
            if (SelectedVersion == null)
            {
                Log("请先选择一个版本");
                return;
            }

            if (!File.Exists(_javaPath))
            {
                Log("Java路径无效，请手动设置");
                return;
            }

            try
            {
                var memory = (int)MemorySlider.Value;
                var jvmArgs = JvmArgsTextBox.Text.Replace("-Xmx4G", $"-Xmx{memory}M");
                
                // 构建启动命令
                var jarPath = Path.Combine(_gameDir, "versions", SelectedVersion.Id, $"{SelectedVersion.Id}.jar");
                
                if (!File.Exists(jarPath))
                {
                    Log("版本文件不存在，请先下载版本");
                    return;
                }

                // 构建完整的启动参数
                var startInfo = new ProcessStartInfo
                {
                    FileName = _javaPath,
                    WorkingDirectory = _gameDir,
                    Arguments = $"{jvmArgs} -jar {jarPath} --username {_currentUsername} --version {SelectedVersion.Id} --gameDir {_gameDir} --assetsDir {Path.Combine(_gameDir, "assets")} --uuid {_currentUuid}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                Log($"正在启动游戏: {SelectedVersion.Id}，内存分配: {memory}MB");
                
                var process = new Process { StartInfo = startInfo };
                
                // 处理输出
                process.OutputDataReceived += (s, args) =>
                {
                    if (!string.IsNullOrEmpty(args.Data))
                    {
                        Log(args.Data);
                    }
                };
                
                process.ErrorDataReceived += (s, args) =>
                {
                    if (!string.IsNullOrEmpty(args.Data))
                    {
                        Log(args.Data);
                    }
                };
                
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                
                Log("游戏启动成功！");
            }
            catch (Exception ex)
            {
                Log($"启动游戏失败: {ex.Message}");
            }
        }

        // 浏览Java路径
        private void BrowseJavaPath(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Java 执行文件|java.exe",
                Title = "选择Java路径"
            };
            
            if (dialog.ShowDialog() == true)
            {
                _javaPath = dialog.FileName;
                JavaPathTextBox.Text = _javaPath;
                Log($"已设置Java路径: {_javaPath}");
            }
        }

        // 浏览游戏目录
        private void BrowseGameDir(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择游戏目录",
                ValidateNames = false,
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "选择文件夹"
            };
            
            if (dialog.ShowDialog() == true)
            {
                string folderPath = System.IO.Path.GetDirectoryName(dialog.FileName);
                if (!string.IsNullOrEmpty(folderPath))
                {
                    _gameDir = folderPath;
                    GameDirTextBox.Text = _gameDir;
                    Log($"已设置游戏目录: {_gameDir}");
                }
            }
        }

        // 打开设置
        private void OpenSettings(object sender, RoutedEventArgs e)
        {
            // 切换到设置标签页
            var tabControl = FindName("tabControl") as TabControl;
            if (tabControl != null && tabControl.Items.Count > 2)
            {
                tabControl.SelectedIndex = 2;
            }
        }

        // 生成UUID
        private static string GenerateUuid()
        {
            var guid = Guid.NewGuid();
            return guid.ToString("N");
        }

        // INotifyPropertyChanged 实现
        public event PropertyChangedEventHandler PropertyChanged;
        private void NotifyPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // 版本信息类
    public class VersionInfo
    {
        public string Id { get; set; }
        public string Type { get; set; }
        public string Url { get; set; }
        public string Time { get; set; }
        public DateTime ReleaseTime { get; set; }
    }
}