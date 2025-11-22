using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
namespace AdbFileUploader
{
    public partial class MainForm : Form
    {
        private string _adbPath;
        private List<string> _uploadFiles = new List<string>();
        private ListBox lstUploadFiles;
        private Button btnAddFile;
        private Button btnAddDirectory;
        private Button btnRemoveFile;
        private Label label5;
        private Label lblFileCount;
        private List<PluginConfig> _plugins = new List<PluginConfig>();
        private const string IniSection = "Plugin";
        // 安卓常用媒体目录（标准系统路径）
        private readonly string[] _commonTargetPaths = new[]
        {
            "/sdcard/Documents/",       // 文档目录
            "/sdcard/Pictures/",        // 图片目录
            "/sdcard/DCIM/Camera/",     // 相机照片目录
            "/sdcard/Movies/",          // 视频目录
            "/sdcard/Music/",           // 音频目录
            "/sdcard/Download/",        // 下载目录
            "/sdcard/Recordings/",      // 录音目录
            "/sdcard/"                  // 根目录（默认）
        };
        public class PluginConfig
        {
            // 文件夹名称（可选，用于标识来源）
            public string FolderName { get; set; }
            // 配置参数
            public string Name { get; set; }
            public string DisplayName { get; set; }
            public string ExePath { get; set; }
            public string CommandLine { get; set; }
        }
        public static class IniHelper
        {
            // 引入Windows API（读取ini文件）
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
            private static extern int GetPrivateProfileString(
                string lpAppName,       // 节名（ini文件中的[节名]）
                string lpKeyName,       // 键名
                string lpDefault,       // 默认值（键不存在时返回）
                StringBuilder lpReturnedString,  // 接收结果的缓冲区
                int nSize,              // 缓冲区大小
                string lpFileName       // ini文件路径
            );

            /// <summary>
            /// 读取ini文件中指定节和键的值
            /// </summary>
            /// <param name="filePath">ini文件路径</param>
            /// <param name="section">节名（如[Plugin]）</param>
            /// <param name="key">键名（如name）</param>
            /// <returns>键对应的值（不存在则返回空）</returns>
            public static string ReadValue(string filePath, string section, string key)
            {
                if (!System.IO.File.Exists(filePath))
                    return string.Empty;

                // 定义缓冲区（避免值过长被截断，可根据需求调整大小）
                StringBuilder buffer = new StringBuilder(2048);
                // 调用API读取值
                GetPrivateProfileString(section, key, "", buffer, buffer.Capacity, filePath);
                return buffer.ToString();
            }
        }
        public MainForm()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            InitializeComponent();
            InitializeAdbPath();
            InitializePlugins();
            CheckForConnectedDevices();
            UpdateFileList();
            this.Icon = new Icon("icon.ico");
            // 初始化提示说明默认文本
            txtTips.Text = "传输文件前，请先按照下列步骤开启ADB调试：\r\n1. 打开青鹿-头像-联系我们，长按电话号码，选择拨打电话。\r\n2. 输入*#*#83781#*#*\r\n在上方菜单中的第一项 TELEPHONY 中下滑找到 USB接口激活，打开\r\n在第二项DEBUG&LOG中找到USB Debug，打开\r\n3. 使用数据线连接电脑，打开本程序，平板应该会提示：是否使用本台计算机调试，勾选一律使用，点确定\r\n4. 重启软件，Having fun \r\n 彩蛋：这个提示可以删除";
        }
        //插件
        private void InitializePlugins()
        {
            try
            {
                string appDirectory = Application.StartupPath;
                string PluginsPath = Path.Combine(appDirectory, "plugins");
                if (!Directory.Exists(PluginsPath))
                {
                    return;
                }
                string[] AllSubfolers = Directory.GetDirectories(PluginsPath, "*", SearchOption.AllDirectories);
                foreach (string h in AllSubfolers)
                {
                    string cfgPath = Path.Combine(h, "config.wtx");
                    if (!File.Exists(cfgPath)) continue;
                    PluginConfig config = new PluginConfig
                    {
                        FolderName = Path.GetDirectoryName(cfgPath),
                        Name = IniHelper.ReadValue(cfgPath, IniSection, "name"),
                        DisplayName = IniHelper.ReadValue(cfgPath, IniSection, "displayname"),
                        ExePath = IniHelper.ReadValue(cfgPath, IniSection, "exepath"),
                        CommandLine = IniHelper.ReadValue(cfgPath, IniSection, "commandline")
                    };
                    _plugins.Add(config);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"读取配置出错：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            // 初始化插件菜单
            InitializePluginMenus();
        }
        private void InitializePluginMenus()
        {

            // 查找或创建「插件」顶级菜单项（避免重复添加）
            ToolStripMenuItem pluginTopMenu = menuStrip.Items
                .OfType<ToolStripMenuItem>()
                .FirstOrDefault(item => item.Text == "插件");

            // 若不存在，则创建「插件」顶级菜单项并添加到menuStrip
            if (pluginTopMenu == null)
            {
                pluginTopMenu = new ToolStripMenuItem("插件")
                {
                    Name = "pluginTopMenu",
                    Size = new System.Drawing.Size(56, 21)
                };
                menuStrip.Items.Insert(menuStrip.Items.Count - 1, pluginTopMenu);
            }

            // 3. 清空「插件」菜单的现有子项（避免重复添加）
            pluginTopMenu.DropDownItems.Clear();

            // 4. 无插件时显示禁用提示
            if (_plugins == null || _plugins.Count == 0)
            {
                ToolStripMenuItem noPluginItem = new ToolStripMenuItem("无可用插件")
                {
                    Enabled = false, // 禁用，防止点击
                    Size = new System.Drawing.Size(152, 22) // 统一子项尺寸
                };
                pluginTopMenu.DropDownItems.Add(noPluginItem);
                return;
            }
            foreach (var plugin in _plugins)
            {
                string menuText = string.IsNullOrEmpty(plugin.DisplayName)
                    ? string.IsNullOrEmpty(plugin.Name)
                        ? "未命名插件"
                        : plugin.Name
                    : plugin.DisplayName;

                // 创建插件子菜单项
                ToolStripMenuItem pluginMenuItem = new ToolStripMenuItem(menuText)
                {
                    Size = new System.Drawing.Size(152, 22),
                    ToolTipText = $"插件目录：{plugin.FolderName}"
                };

                // 6. 绑定点击事件：执行命令行
                pluginMenuItem.Click += (sender, e) =>
                {
                    try
                    {
                        string DownloadPath = Path.Combine(plugin.FolderName, "Downloads");
                        string downloadPath = Path.Combine(plugin.FolderName, "downloads");
                        try
                        {
                            if (Directory.Exists(downloadPath))
                            {
                                // 删除文件
                                foreach (var file in Directory.GetFiles(downloadPath))
                                {
                                    File.Delete(file);
                                }

                                // 删除子目录
                                foreach (var dir in Directory.GetDirectories(downloadPath))
                                {
                                    Directory.Delete(dir, true);
                                }
                            }
                            if (Directory.Exists(DownloadPath))
                            {
                                downloadPath = DownloadPath;
                                // 删除文件
                                foreach (var file in Directory.GetFiles(DownloadPath))
                                {
                                    File.Delete(file);
                                }

                                // 删除子目录
                                foreach (var dir in Directory.GetDirectories(DownloadPath))
                                {
                                    Directory.Delete(dir, true);
                                }
                            }
                            if ((!Directory.Exists(downloadPath)) && (!Directory.Exists(downloadPath)))
                            {
                                Directory.CreateDirectory(downloadPath);
                            }
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"清理下载目录失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }

                        // 校验配置：ExePath和CommandLine不能同时为空
                        if (string.IsNullOrEmpty(plugin.ExePath) && string.IsNullOrEmpty(plugin.CommandLine))
                        {
                            MessageBox.Show($"插件「{menuText}」未配置可执行程序或命令行！", "配置错误",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }
                        // 构建进程启动信息（适配两种配置场景）
                        ProcessStartInfo startInfo = new ProcessStartInfo
                        {
                            // EXEPATH配置的情况下会使用exepath启动程序，使用CommandLine作为参数
                            // 如果EXEPath没有配置，那么就会直接使用CommandLine启动
                            FileName = !string.IsNullOrEmpty(plugin.ExePath) ? plugin.ExePath : plugin.CommandLine,
                            Arguments = !string.IsNullOrEmpty(plugin.ExePath) ? (plugin.CommandLine ?? string.Empty) : string.Empty,
                            WindowStyle = ProcessWindowStyle.Normal, // 正常显示程序窗口
                            UseShellExecute = true, // 允许启动外部程序（解决部分权限问题）
                            WorkingDirectory = plugin.FolderName // 工作目录设为插件目录
                        };

                        // 启动进程执行命令
                        var process = Process.Start(startInfo);
                        var thread = new System.Threading.Thread(() =>
                        {
                            process.WaitForExit();
                            // 进程退出后，将下载的文件添加到上传列表
                            this.Invoke(new Action(() =>
                            {
                                if (!Directory.Exists(downloadPath)) return;

                                var newFiles = Directory.GetFiles(downloadPath)
                                                       .Where(f => !_uploadFiles.Contains(f))
                                                       .ToList();

                                if (newFiles.Count > 0)
                                {
                                    this.Invoke(new Action(() =>
                                    {
                                        foreach (var file in newFiles)
                                        {
                                            _uploadFiles.Add(file);
                                        }
                                        UpdateFileList();
                                        MessageBox.Show($"成功添加 {newFiles.Count} 个文件到上传列表", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                                    }));
                                }
                                else
                                {
                                    this.Invoke(new Action(() =>
                                    {
                                        MessageBox.Show("没有找到新下载的文件", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                                    }));
                                }
                            }));
                        });
                        thread.IsBackground = true;
                        thread.Start();
                    }
                    catch (Exception ex)
                    {
                        // 异常提示：明确插件名称和错误原因
                        MessageBox.Show($"启动插件「{menuText}」失败：\n{ex.Message}\n请检查插件路径和命令行配置！",
                            "执行错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                };

                // 7. 将子菜单项添加到「插件」顶级菜单
                pluginTopMenu.DropDownItems.Add(pluginMenuItem);
            }
        }
        //按钮处理
        private void btnAddFile_Click(object sender, EventArgs e)
        {
            using (var openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "所有文件 (*.*)|*.*|文档文件 (*.doc;*.docx;*.pdf;*.txt)|*.doc;*.docx;*.pdf;*.txt|图片文件 (*.jpg;*.png;*.gif)|*.jpg;*.png;*.gif|视频文件 (*.mp4;*.avi;*.mov)|*.mp4;*.avi;*.mov|音频文件 (*.mp3;*.wav;*.flac)|*.mp3;*.wav;*.flac";
                openFileDialog.Multiselect = true;
                openFileDialog.RestoreDirectory = true;

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    foreach (var file in openFileDialog.FileNames)
                    {
                        if (!_uploadFiles.Contains(file))
                        {
                            _uploadFiles.Add(file);
                        }
                    }
                    UpdateFileList();
                }
            }
        }
        private void btnAddDirectory_Click(object sender, EventArgs e)
        {
            using (var folderDialog = new FolderBrowserDialog())
            {
                if (folderDialog.ShowDialog() == DialogResult.OK)
                {
                    if (!_uploadFiles.Contains(folderDialog.SelectedPath))
                    {
                        _uploadFiles.Add(folderDialog.SelectedPath);
                    }
                    UpdateFileList();
                }
            }
        }
        private void btnRemoveFile_Click(object sender, EventArgs e)
        {
            if (lstUploadFiles.SelectedItems.Count > 0)
            {
                var itemsToRemove = lstUploadFiles.SelectedItems.Cast<string>().ToList();
                foreach (var item in itemsToRemove)
                {
                    _uploadFiles.Remove(item);
                }
                UpdateFileList();
            }
        }
        private void btnSelectFile_Click(object sender, EventArgs e)
        {
            using (var openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "所有文件 (*.*)|*.*|文档文件 (*.doc;*.docx;*.pdf;*.txt)|*.doc;*.docx;*.pdf;*.txt|图片文件 (*.jpg;*.png;*.gif)|*.jpg;*.png;*.gif|视频文件 (*.mp4;*.avi;*.mov)|*.mp4;*.avi;*.mov|音频文件 (*.mp3;*.wav;*.flac)|*.mp3;*.wav;*.flac";
                openFileDialog.Multiselect = true;
                openFileDialog.RestoreDirectory = true;

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    foreach (var file in openFileDialog.FileNames)
                    {
                        if (!_uploadFiles.Contains(file))
                        {
                            _uploadFiles.Add(file);
                        }
                    }
                    UpdateFileList();
                }
            }
        }
        private void btnRefreshDevices_Click(object sender, EventArgs e)
        {
            CheckForConnectedDevices();
        }
        private void btnUpload_Click(object sender, EventArgs e)
        {
            if (_uploadFiles.Count == 0)
            {
                MessageBox.Show("请至少添加一个文件或目录", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (lstDevices.SelectedItem == null)
            {
                MessageBox.Show("请选择一个设备", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 从下拉框获取目标路径（支持手动输入）
            var targetPath = cboTargetPath.Text.Trim();
            if (string.IsNullOrEmpty(targetPath))
            {
                targetPath = "/sdcard/";
                cboTargetPath.Text = targetPath;
            }

            if (!targetPath.EndsWith("/"))
                targetPath += "/";

            progressBar.Value = 0;
            lblStatus.Text = "准备上传...";
            btnUpload.Enabled = false;
            btnAddFile.Enabled = false;
            btnAddDirectory.Enabled = false;
            btnRemoveFile.Enabled = false;
            btnRefreshDevices.Enabled = false;
            cboTargetPath.Enabled = false;
            txtTips.Enabled = false;

            var uploadThread = new System.Threading.Thread(() =>
            {
                try
                {
                    int totalFiles = _uploadFiles.Count;
                    int currentFile = 0;

                    foreach (var sourcePath in _uploadFiles)
                    {
                        currentFile++;
                        Invoke(new Action(() =>
                        {
                            lblStatus.Text = $"正在上传 ({currentFile}/{totalFiles})...";
                            progressBar.Value = (int)((double)currentFile / totalFiles * 100);
                        }));

                        var fileName = Path.GetFileName(sourcePath);
                        var fullTargetPath = $"{targetPath}{fileName}";

                        // 检查是文件还是目录
                        bool isDirectory = Directory.Exists(sourcePath);

                        string arguments;
                        if (isDirectory)
                        {
                            arguments = $"push \"{sourcePath}\" \"{targetPath}\"";
                        }
                        else
                        {
                            arguments = $"push \"{sourcePath}\" \"{fullTargetPath}\"";
                        }

                        var process = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = _adbPath,
                                Arguments = arguments,
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                CreateNoWindow = true
                            }
                        };

                        string output = "";
                        string error = "";

                        process.OutputDataReceived += (s, args) => { if (args.Data != null) output += args.Data + Environment.NewLine; };
                        process.ErrorDataReceived += (s, args) => { if (args.Data != null) error += args.Data + Environment.NewLine; };

                        process.Start();
                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();
                        process.WaitForExit();

                        if (process.ExitCode != 0)
                        {
                            Invoke(new Action(() =>
                            {
                                lblStatus.Text = $"部分上传失败: {error}";
                                MessageBox.Show($"文件/目录 '{fileName}' 上传失败: {error}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            }));
                        }
                    }

                    Invoke(new Action(() =>
                    {
                        progressBar.Value = 100;
                        lblStatus.Text = $"上传完成: 共 {totalFiles} 个文件/目录";
                        MessageBox.Show($"已成功上传 {totalFiles} 个文件/目录到\n{targetPath}", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);

                        btnUpload.Enabled = true;
                        btnAddFile.Enabled = true;
                        btnAddDirectory.Enabled = true;
                        btnRemoveFile.Enabled = true;
                        btnRefreshDevices.Enabled = true;
                        cboTargetPath.Enabled = true;
                        txtTips.Enabled = true;
                    }));
                }
                catch (Exception ex)
                {
                    Invoke(new Action(() =>
                    {
                        lblStatus.Text = $"上传出错: {ex.Message}";
                        MessageBox.Show($"上传出错: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);

                        btnUpload.Enabled = true;
                        btnAddFile.Enabled = true;
                        btnAddDirectory.Enabled = true;
                        btnRemoveFile.Enabled = true;
                        btnRefreshDevices.Enabled = true;
                        cboTargetPath.Enabled = true;
                        txtTips.Enabled = true;
                    }));
                }
            });

            uploadThread.Start();
        }
        //工具函数
        private void UpdateFileList()
        {
            lstUploadFiles.Items.Clear();
            lstUploadFiles.Items.AddRange(_uploadFiles.ToArray());
            lblFileCount.Text = $"已选择 {_uploadFiles.Count} 项";
            btnUpload.Enabled = lstDevices.Items.Count > 0 && _uploadFiles.Count > 0;
        }
        private void InitializeAdbPath()
        {
            _adbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "adb.exe");

            if (!File.Exists(_adbPath))
            {
                var pathEnv = Environment.GetEnvironmentVariable("PATH");
                if (pathEnv != null)
                {
                    foreach (var path in pathEnv.Split(';'))
                    {
                        var testPath = Path.Combine(path, "adb.exe");
                        if (File.Exists(testPath))
                        {
                            _adbPath = testPath;
                            break;
                        }
                    }
                }

                if (!File.Exists(_adbPath))
                {
                    lblStatus.Text = "未找到ADB，请将adb.exe放在程序目录下";
                    btnUpload.Enabled = false;
                }
            }
        }
        private void CheckForConnectedDevices()
        {
            if (!File.Exists(_adbPath)) return;

            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _adbPath,
                        Arguments = "devices",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                lstDevices.Items.Clear();
                var lines = output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    if (line.Contains("\tdevice") && !line.Contains("List of devices"))
                    {
                        lstDevices.Items.Add(line.Split('\t')[0]);
                    }
                }

                if (lstDevices.Items.Count > 0)
                {
                    lstDevices.SelectedIndex = 0;
                    lblStatus.Text = $"找到 {lstDevices.Items.Count} 个设备";
                    btnUpload.Enabled = _uploadFiles.Count > 0; // 仅当有文件且有设备时启用上传
                }
                else
                {
                    lblStatus.Text = "未找到连接的设备，请确保设备已启用调试模式";
                    btnUpload.Enabled = false;
                }
            }
            catch (Exception ex)
            {
                lblStatus.Text = $"检查设备时出错: {ex.Message}";
            }
        }
        

        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }
        private void uploadToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // 触发上传按钮的点击事件
            btnUpload_Click(sender, e);
        }
        //常驻菜单
        private void neteaseMusicToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (var downloaderForm = new NeteaseMusicDownloaderForm())
            {
                downloaderForm.FilesDownloaded += (files) =>
                {
                    // 将下载的文件添加到上传列表
                    foreach (var file in files)
                    {
                        if (!_uploadFiles.Contains(file))
                        {
                            _uploadFiles.Add(file);
                        }
                    }
                    UpdateFileList();
                };
                downloaderForm.ShowDialog(this);
            }
        }
        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Close();
        }
        private void InitializeComponent()
        {
            this.label2 = new System.Windows.Forms.Label();
            this.lstDevices = new System.Windows.Forms.ListBox();
            this.btnRefreshDevices = new System.Windows.Forms.Button();
            this.label3 = new System.Windows.Forms.Label();
            this.btnUpload = new System.Windows.Forms.Button();
            this.progressBar = new System.Windows.Forms.ProgressBar();
            this.lblStatus = new System.Windows.Forms.Label();
            this.cboTargetPath = new System.Windows.Forms.ComboBox();
            this.label4 = new System.Windows.Forms.Label();
            this.txtTips = new System.Windows.Forms.TextBox();
            this.panel1 = new System.Windows.Forms.Panel();
            this.lblFileCount = new System.Windows.Forms.Label();
            this.label5 = new System.Windows.Forms.Label();
            this.lstUploadFiles = new System.Windows.Forms.ListBox();
            this.btnRemoveFile = new System.Windows.Forms.Button();
            this.btnAddDirectory = new System.Windows.Forms.Button();
            this.btnAddFile = new System.Windows.Forms.Button();
            this.panel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.label2.Location = new System.Drawing.Point(23, 200);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(56, 17);
            this.label2.TabIndex = 6;
            this.label2.Text = "连接设备";
            // 
            // lstDevices
            // 
            this.lstDevices.BackColor = System.Drawing.Color.White;
            this.lstDevices.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.lstDevices.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(68)))), ((int)(((byte)(68)))), ((int)(((byte)(68)))));
            this.lstDevices.FormattingEnabled = true;
            this.lstDevices.ItemHeight = 17;
            this.lstDevices.Location = new System.Drawing.Point(23, 223);
            this.lstDevices.Name = "lstDevices";
            this.lstDevices.Size = new System.Drawing.Size(440, 87);
            this.lstDevices.TabIndex = 7;
            // 
            // btnRefreshDevices
            // 
            this.btnRefreshDevices.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(92)))), ((int)(((byte)(184)))), ((int)(((byte)(92)))));
            this.btnRefreshDevices.FlatAppearance.BorderSize = 0;
            this.btnRefreshDevices.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnRefreshDevices.ForeColor = System.Drawing.Color.White;
            this.btnRefreshDevices.Location = new System.Drawing.Point(470, 223);
            this.btnRefreshDevices.Name = "btnRefreshDevices";
            this.btnRefreshDevices.Size = new System.Drawing.Size(80, 28);
            this.btnRefreshDevices.TabIndex = 8;
            this.btnRefreshDevices.Text = "刷新设备";
            this.btnRefreshDevices.UseVisualStyleBackColor = false;
            this.btnRefreshDevices.Click += new System.EventHandler(this.btnRefreshDevices_Click);
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.label3.Location = new System.Drawing.Point(23, 340);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(56, 17);
            this.label3.TabIndex = 9;
            this.label3.Text = "目标路径";
            // 
            // btnUpload
            // 
            this.btnUpload.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(230)))), ((int)(((byte)(126)))), ((int)(((byte)(34)))));
            this.btnUpload.FlatAppearance.BorderSize = 0;
            this.btnUpload.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnUpload.ForeColor = System.Drawing.Color.White;
            this.btnUpload.Location = new System.Drawing.Point(470, 404);
            this.btnUpload.Name = "btnUpload";
            this.btnUpload.Size = new System.Drawing.Size(80, 32);
            this.btnUpload.TabIndex = 11;
            this.btnUpload.Text = "开始上传";
            this.btnUpload.UseVisualStyleBackColor = false;
            this.btnUpload.Click += new System.EventHandler(this.btnUpload_Click);
            // 
            // progressBar
            // 
            this.progressBar.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(230)))), ((int)(((byte)(126)))), ((int)(((byte)(34)))));
            this.progressBar.Location = new System.Drawing.Point(23, 404);
            this.progressBar.Name = "progressBar";
            this.progressBar.Size = new System.Drawing.Size(440, 32);
            this.progressBar.Style = System.Windows.Forms.ProgressBarStyle.Continuous;
            this.progressBar.TabIndex = 12;
            // 
            // lblStatus
            // 
            this.lblStatus.AutoSize = true;
            this.lblStatus.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(102)))), ((int)(((byte)(102)))), ((int)(((byte)(102)))));
            this.lblStatus.Location = new System.Drawing.Point(23, 446);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(32, 17);
            this.lblStatus.TabIndex = 13;
            this.lblStatus.Text = "就绪";
            // 
            // cboTargetPath
            // 
            this.cboTargetPath.BackColor = System.Drawing.Color.White;
            this.cboTargetPath.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(68)))), ((int)(((byte)(68)))), ((int)(((byte)(68)))));
            this.cboTargetPath.Location = new System.Drawing.Point(23, 363);
            this.cboTargetPath.Name = "cboTargetPath";
            this.cboTargetPath.Size = new System.Drawing.Size(527, 25);
            this.cboTargetPath.TabIndex = 10;
            this.cboTargetPath.Text = "/sdcard/";
            this.cboTargetPath.Items.AddRange(_commonTargetPaths);
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.label4.Location = new System.Drawing.Point(23, 473);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(56, 17);
            this.label4.TabIndex = 14;
            this.label4.Text = "提示说明";
            // 
            // txtTips
            // 
            this.txtTips.BackColor = System.Drawing.Color.White;
            this.txtTips.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.txtTips.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(68)))), ((int)(((byte)(68)))), ((int)(((byte)(68)))));
            this.txtTips.Location = new System.Drawing.Point(23, 496);
            this.txtTips.Multiline = true;
            this.txtTips.Name = "txtTips";
            this.txtTips.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtTips.Size = new System.Drawing.Size(527, 100);
            this.txtTips.TabIndex = 15;
            // 
            // menuStrip
            // 
            this.menuStrip = new System.Windows.Forms.MenuStrip();
            this.menuStrip.Location = new System.Drawing.Point(0, 0);
            this.menuStrip.Name = "menuStrip";
            this.menuStrip.Size = new System.Drawing.Size(580, 25);
            this.menuStrip.TabIndex = 16;
            this.menuStrip.Text = "menuStrip";


            // 
            // neteaseMusicToolStripMenuItem
            // 
            this.neteaseMusicToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.neteaseMusicToolStripMenuItem.Name = "neteaseMusicToolStripMenuItem";
            this.neteaseMusicToolStripMenuItem.Size = new System.Drawing.Size(140, 21);
            this.neteaseMusicToolStripMenuItem.Text = "从网易云音乐下载";
            this.neteaseMusicToolStripMenuItem.Click += new System.EventHandler(this.neteaseMusicToolStripMenuItem_Click);

            // 
            // exitToolStripMenuItem
            // 
            this.exitToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.exitToolStripMenuItem.Name = "exitToolStripMenuItem";
            this.exitToolStripMenuItem.Size = new System.Drawing.Size(56, 21);
            this.exitToolStripMenuItem.Text = "退出";
            this.exitToolStripMenuItem.Click += new System.EventHandler(this.exitToolStripMenuItem_Click);

            // 将菜单项直接添加到菜单栏（不是作为子项）
            this.menuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
                this.neteaseMusicToolStripMenuItem,
                this.exitToolStripMenuItem});

            // 将菜单栏添加到窗体
            this.MainMenuStrip = this.menuStrip;
            this.Controls.Add(this.menuStrip);
            // 
            // panel1
            // 
            this.panel1.BackColor = System.Drawing.Color.Transparent;
            this.panel1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.panel1.Controls.Add(this.lblFileCount);
            this.panel1.Controls.Add(this.label5);
            this.panel1.Controls.Add(this.lstUploadFiles);
            this.panel1.Controls.Add(this.btnRemoveFile);
            this.panel1.Controls.Add(this.btnAddDirectory);
            this.panel1.Controls.Add(this.btnAddFile);
            this.panel1.Controls.Add(this.txtTips);
            this.panel1.Controls.Add(this.label4);
            this.panel1.Controls.Add(this.cboTargetPath);
            this.panel1.Controls.Add(this.lblStatus);
            this.panel1.Controls.Add(this.progressBar);
            this.panel1.Controls.Add(this.btnUpload);
            this.panel1.Controls.Add(this.label3);
            this.panel1.Controls.Add(this.btnRefreshDevices);
            this.panel1.Controls.Add(this.lstDevices);
            this.panel1.Controls.Add(this.label2);
            this.panel1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panel1.Location = new System.Drawing.Point(0, 0);
            this.panel1.Name = "panel1";
            this.panel1.Padding = new System.Windows.Forms.Padding(20);
            this.panel1.Size = new System.Drawing.Size(580, 680);
            this.panel1.TabIndex = 0;
            // 
            // lblFileCount
            // 
            this.lblFileCount.AutoSize = true;
            this.lblFileCount.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(102)))), ((int)(((byte)(102)))), ((int)(((byte)(102)))));
            this.lblFileCount.Location = new System.Drawing.Point(23, 176);
            this.lblFileCount.Name = "lblFileCount";
            this.lblFileCount.Size = new System.Drawing.Size(71, 17);
            this.lblFileCount.TabIndex = 5;
            this.lblFileCount.Text = "已选择 0 项";
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.label5.Location = new System.Drawing.Point(23, 23);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(80, 17);
            this.label5.TabIndex = 0;
            this.label5.Text = "上传文件列表";
            // 
            // lstUploadFiles
            // 
            this.lstUploadFiles.BackColor = System.Drawing.Color.White;
            this.lstUploadFiles.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.lstUploadFiles.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(68)))), ((int)(((byte)(68)))), ((int)(((byte)(68)))));
            this.lstUploadFiles.FormattingEnabled = true;
            this.lstUploadFiles.ItemHeight = 17;
            this.lstUploadFiles.Location = new System.Drawing.Point(23, 46);
            this.lstUploadFiles.Name = "lstUploadFiles";
            this.lstUploadFiles.SelectionMode = System.Windows.Forms.SelectionMode.MultiExtended;
            this.lstUploadFiles.Size = new System.Drawing.Size(440, 104);
            this.lstUploadFiles.TabIndex = 1;
            // 
            // btnRemoveFile
            // 
            this.btnRemoveFile.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(231)))), ((int)(((byte)(76)))), ((int)(((byte)(60)))));
            this.btnRemoveFile.FlatAppearance.BorderSize = 0;
            this.btnRemoveFile.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnRemoveFile.ForeColor = System.Drawing.Color.White;
            this.btnRemoveFile.Location = new System.Drawing.Point(470, 114);
            this.btnRemoveFile.Name = "btnRemoveFile";
            this.btnRemoveFile.Size = new System.Drawing.Size(80, 28);
            this.btnRemoveFile.TabIndex = 4;
            this.btnRemoveFile.Text = "删除";
            this.btnRemoveFile.UseVisualStyleBackColor = false;
            this.btnRemoveFile.Click += new System.EventHandler(this.btnRemoveFile_Click);
            // 
            // btnAddDirectory
            // 
            this.btnAddDirectory.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(46)))), ((int)(((byte)(204)))), ((int)(((byte)(113)))));
            this.btnAddDirectory.FlatAppearance.BorderSize = 0;
            this.btnAddDirectory.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnAddDirectory.ForeColor = System.Drawing.Color.White;
            this.btnAddDirectory.Location = new System.Drawing.Point(470, 80);
            this.btnAddDirectory.Name = "btnAddDirectory";
            this.btnAddDirectory.Size = new System.Drawing.Size(80, 28);
            this.btnAddDirectory.TabIndex = 3;
            this.btnAddDirectory.Text = "添加目录";
            this.btnAddDirectory.UseVisualStyleBackColor = false;
            this.btnAddDirectory.Click += new System.EventHandler(this.btnAddDirectory_Click);
            // 
            // btnAddFile
            // 
            this.btnAddFile.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(152)))), ((int)(((byte)(219)))));
            this.btnAddFile.FlatAppearance.BorderSize = 0;
            this.btnAddFile.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnAddFile.ForeColor = System.Drawing.Color.White;
            this.btnAddFile.Location = new System.Drawing.Point(470, 46);
            this.btnAddFile.Name = "btnAddFile";
            this.btnAddFile.Size = new System.Drawing.Size(80, 28);
            this.btnAddFile.TabIndex = 2;
            this.btnAddFile.Text = "添加文件";
            this.btnAddFile.UseVisualStyleBackColor = false;
            this.btnAddFile.Click += new System.EventHandler(this.btnAddFile_Click);
            // 
            // MainForm
            // 
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(245)))), ((int)(((byte)(247)))), ((int)(((byte)(250)))));
            this.ClientSize = new System.Drawing.Size(580, 680);
            this.Controls.Add(this.panel1);
            this.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(64)))), ((int)(((byte)(80)))));
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.Name = "MainForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "WiredTransfereXpediteQuickRemoteQuest";
            this.panel1.ResumeLayout(false);
            this.panel1.PerformLayout();
            this.ResumeLayout(false);

        }
        // 控件声明
        private Label label2;
        private ListBox lstDevices;
        private Button btnRefreshDevices;
        private Label label3;
        private Button btnUpload;
        private ProgressBar progressBar;
        private Label lblStatus;
        private ComboBox cboTargetPath;
        private Label label4;
        private TextBox txtTips;
        private Panel panel1;
        private MenuStrip menuStrip;
        private ToolStripMenuItem neteaseMusicToolStripMenuItem;
        private ToolStripMenuItem exitToolStripMenuItem;
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // 设置应用程序处理异常的方式
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                MessageBox.Show($"发生未处理的异常: {e.Exception.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            Application.Run(new MainForm());
        }
    }
}