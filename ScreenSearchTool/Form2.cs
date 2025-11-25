using System;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Windows.Forms;

namespace ScreenSearchTool
{
    public partial class Form2 : Form
    {
        private WebClient webClient;
        private string downloadUrl = "https://github.com/toan704/ScreenSearchTool/releases/download/tf/Lib_win-x64.zip";
        private string zipFilePath;
        private string extractPath;

        public Form2()
        {
            InitializeComponent();
            zipFilePath = Path.Combine(Application.StartupPath, "Lib-support_win-x64.zip");
            extractPath = Application.StartupPath;
        }

        private void Form2_Load(object sender, EventArgs e)
        {
            LogStatus("Kiểm tra thư viện...");
            StartDownload();
        }

        private void StartDownload()
        {
            try
            {
                LogStatus("Khởi tạo...");
                webClient = new WebClient();
                webClient.DownloadProgressChanged += WebClient_DownloadProgressChanged;
                webClient.DownloadFileCompleted += WebClient_DownloadFileCompleted;
                webClient.DownloadFileAsync(new Uri(downloadUrl), zipFilePath);
            }
            catch (Exception ex)
            {
                LogStatus($"Lỗi: {ex.Message}");
                this.Close();
            }
        }

        private void WebClient_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            progressBar1.Value = e.ProgressPercentage;
        }

        private void WebClient_DownloadFileCompleted(object sender, AsyncCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                LogStatus($"Lỗi: {e.Error.Message}");
                return;
            }

            if (e.Cancelled)
            {
                LogStatus("Đã hủy");
                return;
            }

            LogStatus("Hoàn thành");
            VerifyAndExtractZip();
        }

        private void VerifyAndExtractZip()
        {
            try
            {
                // Kiểm tra file
                FileInfo fileInfo = new FileInfo(zipFilePath);
                LogStatus($"{fileInfo.Length / 1024 / 1024}MB");

                // Giải nén
                LogStatus("Đang giải nén...");
                progressBar1.Style = ProgressBarStyle.Marquee;

                using (ZipArchive archive = ZipFile.OpenRead(zipFilePath))
                {
                    LogStatus($"{archive.Entries.Count} files");
                }

                ZipFile.ExtractToDirectory(zipFilePath, extractPath, true);
                LogStatus("Giải nén xong");

                // Xóa file zip
                File.Delete(zipFilePath);
                //LogStatus("Đã xóa file zip");

                // Kiểm tra file đích
                string targetFile = Path.Combine(extractPath, "paddle_inference_c.dll");
                if (File.Exists(targetFile))
                {
                    LogStatus("Thư viện đã sẵn sàng");
                    progressBar1.Style = ProgressBarStyle.Continuous;
                    progressBar1.Value = 100;

                    // Đóng form và mở Form1
                    System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
                    timer.Interval = 1000;
                    timer.Tick += (s, args) =>
                    {
                        timer.Stop();
                        this.DialogResult = DialogResult.OK;
                        this.Close();
                    };
                    timer.Start();
                }
                else
                {
                    LogStatus("Lỗi: Thiếu file thư viện");
                }
            }
            catch (Exception ex)
            {
                LogStatus($"Lỗi giải nén: {ex.Message}");
            }
        }

        private void LogStatus(string message)
        {
            if (lbStatus.InvokeRequired)
            {
                lbStatus.Invoke(new Action<string>(LogStatus), message);
                return;
            }

            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            string logMessage = $"[{timestamp}] {message}";

            lbStatus.Items.Add(logMessage);
            lbStatus.TopIndex = lbStatus.Items.Count - 1;
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            if (webClient != null && webClient.IsBusy)
            {
                webClient.CancelAsync();
            }
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }
    }
}